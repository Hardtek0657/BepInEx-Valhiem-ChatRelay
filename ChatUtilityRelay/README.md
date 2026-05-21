# ChatUtility Relay

Small WebSocket broadcast server for the `ChatUtility` Valheim plugin.

## Run locally

```powershell
dotnet run --project .\ChatUtilityRelay.csproj --urls http://0.0.0.0:8765
```

## Run published exe

```powershell
.\ChatUtilityRelay.exe --urls http://0.0.0.0:5000
```

Plugin config:

```ini
[RelayChat]
ServerUrl = wss://127.0.0.1/chat
```

When using Cloudflare HTTPS, expose the relay through Apache/XAMPP on standard HTTPS port 443 and proxy `/chat` to the local relay process on port 5000. The relay exe itself should still listen with plain HTTP, for example `http://127.0.0.1:5000`.

Players type normal chat in Valheim. The plugin sends normal chat to the relay, and every connected plugin receives it back in-game as `[Relay] PlayerName: message here`. Whispers are left as vanilla Valheim chat.

For public hosting, put this behind a reverse proxy with TLS and use `wss://your-domain/chat`.
