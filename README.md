# Viewer Count Fetcher

This application fetches the live viewer count from a YouTube live broadcast using the YouTube Data API. It handles OAuth 2.0 authentication, including token refresh, and retrieves the current number of viewers for the user's active live broadcast. I use this currently to display my live viewer count as a segment in my terminal prompt while streaming.

## Settings

Create an `appsettings.json` file in the root of the project and supply the settings as shown in the `appsettings.Example.json` file.

## Example Oh-My-Posh Segment

```json
{
  "type": "command",
  "style": "powerline",
  "powerline_symbol": "\ue0b0",
  "foreground": "#ffffff",
  "background": "#ff0033",
  "properties": {
    "shell": "pwsh",
    "command": "dotnet C:/Path/To/viewer-count-fetcher/index.cs"
  },
  "cache": {
    "duration": "30s",
    "strategy": "session"
  },
  "template": " \udb81\uddc3 {{ .Output }} "
}
```
