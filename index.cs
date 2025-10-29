using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

try
{
  var settings = await LoadSettingsAsync();
  var tokenResponse = await GetTokenResponseAsync(settings);
  var viewerCount = await GetLiveViewerCountAsync(tokenResponse);
  Console.Write(viewerCount);
}
catch (Exception ex)
{
  Console.Write(ex.Message);
}

#region Methods

static async Task<int> GetLiveViewerCountAsync(TokenResponse tokenResponse)
{
  using var client = new HttpClient();

  var url = $"https://youtube.googleapis.com/youtube/v3/liveBroadcasts?part=statistics&broadcastStatus=active";

  var requestUri = new Uri(url);

  using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
  request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);

  var response = await client.SendAsync(request);

  if (response.IsSuccessStatusCode is false)
  {
    throw new Exception("Failed to get broadcast");
  }

  var ytResponse = await response.Content.ReadFromJsonAsync(JsonContext.Default.YTResponse);

  if (ytResponse is null || ytResponse.Items.Length is 0)
  {
    return -1;
  }

  return ytResponse.Items[0].Stats.Viewers;
}

static async Task<TokenResponse> GetTokenResponseAsync(Settings settings)
{
  var tokenResponsePath = Path.Combine(Directory.GetCurrentDirectory(), "tokenResponse.json");

  if (File.Exists(tokenResponsePath) is false)
  {
    return await GetAccessTokenAsync(settings, tokenResponsePath);
  }
  else
  {
    var tokenResponseContent = await File.ReadAllTextAsync(tokenResponsePath);
    var existingTokenResponse = JsonSerializer.Deserialize(tokenResponseContent, JsonContext.Default.TokenResponse) ??
      throw new Exception("Failed to parse existing token response");

    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    if (now >= existingTokenResponse.ExpiresAt)
    {
      return await RefreshTokenAsync(existingTokenResponse.RefreshToken, settings, tokenResponsePath);
    }

    return existingTokenResponse;
  }
}

static async Task<Settings> LoadSettingsAsync()
{
  var settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
  var settingsContent = await File.ReadAllTextAsync(settingsPath);
  var settings = JsonSerializer.Deserialize(settingsContent, JsonContext.Default.Settings);

  if (settings is null)
  {
    throw new Exception("Failed to load settings");
  }

  return settings;
}

static async Task<TokenResponse> RefreshTokenAsync(string refreshToken, Settings settings, string tokenResponsePath)
{
  using var client = new HttpClient();

  var uri = new Uri(settings.TokenUri, UriKind.Absolute);

  using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, uri)
  {
    Content = new FormUrlEncodedContent(new Dictionary<string, string?>
    {
      ["client_id"] = settings.ClientId,
      ["client_secret"] = settings.ClientSecret,
      ["refresh_token"] = refreshToken,
      ["grant_type"] = "refresh_token"
    })
  };

  var tokenResponse = await client.SendAsync(tokenRequest);
  var tokenResponseContent = await tokenResponse.Content.ReadAsStringAsync();

  if (tokenResponse.IsSuccessStatusCode is false)
  {
    throw new Exception("Failed to refresh OAuth token");
  }

  var newToken = JsonSerializer.Deserialize(tokenResponseContent, JsonContext.Default.TokenResponse) ??
    throw new Exception("Failed to parse refreshed OAuth token");

  var newTokenResponse = newToken.WithExpiresAt() with { RefreshToken = refreshToken };

  var tokenResponseJson = JsonSerializer.Serialize(newTokenResponse, JsonContext.Default.TokenResponse);
  await File.WriteAllTextAsync(tokenResponsePath, tokenResponseJson);
  return newTokenResponse;
}

static async Task<TokenResponse> GetAccessTokenAsync(Settings settings, string tokenResponsePath)
{
  var authUri = GetOAuthUri(settings);

  using var process = Process.Start(new ProcessStartInfo
  {
    FileName = authUri,
    UseShellExecute = true
  });

  using var listener = new HttpListener();
  listener.Prefixes.Add(settings.RedirectUri);
  listener.Start();

  var listenerContext = await listener.GetContextAsync();
  var oauthCode = listenerContext.Request.QueryString["code"];

  try
  {
    var tokenResponse = await GetTokenAsync(oauthCode, settings);
    var tokenResponseJson = JsonSerializer.Serialize(tokenResponse, JsonContext.Default.TokenResponse);
    await File.WriteAllTextAsync(tokenResponsePath, tokenResponseJson);
    return tokenResponse;
  }
  finally
  {
    var responseHtml = "<html><body><h1>You may now close this window.</h1></body></html>";
    var buffer = Encoding.UTF8.GetBytes(responseHtml);
    listenerContext.Response.ContentLength64 = buffer.Length;
    await listenerContext.Response.OutputStream.WriteAsync(buffer);
    listenerContext.Response.Close();
    listener.Stop();
  }
}

static string GetOAuthUri(Settings settings)
{
  var authUriQueryParams = new Dictionary<string, string>
  {
    ["client_id"] = settings.ClientId,
    ["redirect_uri"] = settings.RedirectUri,
    ["response_type"] = "code",
    ["scope"] = settings.Scopes,
    ["access_type"] = "offline",
    ["prompt"] = "consent",
  };
  var query = string.Join("&", authUriQueryParams.Select(static kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
  var authUri = $"{settings.AuthUri}?{query}";

  return authUri;
}

static async Task<TokenResponse> GetTokenAsync(string? code, Settings settings)
{
  using var client = new HttpClient();

  var uri = new Uri(settings.TokenUri, UriKind.Absolute);

  using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, uri)
  {
    Content = new FormUrlEncodedContent(new Dictionary<string, string?>
    {
      ["code"] = code,
      ["client_id"] = settings.ClientId,
      ["client_secret"] = settings.ClientSecret,
      ["redirect_uri"] = settings.RedirectUri,
      ["grant_type"] = "authorization_code"
    })
  };

  var tokenResponse = await client.SendAsync(tokenRequest);
  var tokenResponseContent = await tokenResponse.Content.ReadAsStringAsync();

  if (tokenResponse.IsSuccessStatusCode is false)
  {
    throw new Exception("Failed to get OAuth token");
  }

  var token = JsonSerializer.Deserialize(tokenResponseContent, JsonContext.Default.TokenResponse) ?? throw new Exception("Failed to parse OAuth token");
  return token.WithExpiresAt();
}

#endregion

#region Models

record Settings(
  string ClientId,
  string ClientSecret,
  string RedirectUri,
  string TokenUri,
  string AuthUri,
  string Scopes
);

record YTResponse(
  [property: JsonPropertyName("items")]
  YTItem[] Items
);

record YTItem(
  [property: JsonPropertyName("statistics")]
  YTStats Stats
);

record YTStats(
  [property: JsonPropertyName("concurrentViewers")]
  int Viewers
);

record TokenResponse(
  [property: JsonPropertyName("access_token")]
  string AccessToken,
  [property: JsonPropertyName("expires_in")]
  int ExpiresIn,
  [property: JsonPropertyName("token_type")]
  string TokenType,
  [property: JsonPropertyName("scope")]
  string Scope,
  [property: JsonPropertyName("refresh_token")]
  string RefreshToken,
  [property: JsonPropertyName("expires_at")]
  long ExpiresAt
)
{
  public TokenResponse WithExpiresAt() => this with
  {
    ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (ExpiresIn * 1000)
  };
}

[JsonSourceGenerationOptions(
  WriteIndented = true, 
  NumberHandling = JsonNumberHandling.AllowReadingFromString
)]
[JsonSerializable(typeof(YTResponse))]
[JsonSerializable(typeof(YTItem))]
[JsonSerializable(typeof(YTStats))]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(TokenResponse))]
internal sealed partial class JsonContext : JsonSerializerContext
{
}

#endregion