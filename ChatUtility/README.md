# ChatUtility

BepInEx plugin for Valheim that enables cross-server global chat relay via WebSocket and provides AFK (away-from-keyboard) status announcements.

## Features

- **Global Chat Relay** — Normal chat messages are relayed through a WebSocket server so players on different servers can chat together in real-time.
- **AFK Announcements** — Automatically notifies other players when you go AFK or return, based on configurable idle time, movement, and rotation thresholds.
- **In-Game Commands** — `/afk` to toggle AFK notifications and `/retryws` to force-reconnect the WebSocket.
- **Configurable** — All settings are exposed through BepInEx configuration and can be tweaked without restarting.

## Installation

1. Install [BepInEx](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) for Valheim.
2. Copy `ChatUtility.dll` into your `BepInEx/plugins/` folder.
3. Launch the game. The config file is generated at `BepInEx/config/local.valheim.chatutility.cfg`.

## Configuration

```ini
[General]
## Enable Chat Utility.
Enabled = true

[RelayChat]
## Relay all normal chat messages through the WebSocket server.
Enabled = true
## WebSocket relay URL.
ServerUrl = wss://127.0.0.1/chat
## Seconds to wait before reconnecting after the relay disconnects.
ReconnectSeconds = 5

[AFK]
## Announce AFK and return status through relay chat.
Enabled = true
## Seconds without input, movement, or rotation before announcing AFK.
IdleSeconds = 300
## Player movement distance that counts as activity.
MovementDistance = 0.25
## Player rotation change that counts as activity.
RotationDegrees = 10
```

## Commands

| Command | Description |
|---------|-------------|
| `/afk enable` | Enable AFK notifications |
| `/afk disable` | Disable AFK notifications |
| `/afk status` | Show current AFK notification state |
| `/retryws` | Force reconnect the WebSocket relay |

## Building

Requires .NET SDK and references to Valheim + BepInEx assemblies (see `.csproj` for hint paths).

```powershell
dotnet build ChatUtility.csproj
```

Output goes to `bin/Debug/netstandard2.1/ChatUtility.dll`.

## How It Works

The plugin patches `Chat.SendText` via Harmony. When a player sends a normal chat message (not a whisper), it's Base64-encoded and sent to the WebSocket relay server as a `CHAT\t<sender>\t<message>` frame. The relay broadcasts it to every connected client, and the plugin renders incoming messages in-game with a distinct magenta color.

AFK tracking monitors input keys, player position, and rotation each frame. After the configured idle time with no activity, a `NOTICE\t...` frame is broadcast to all connected players.

## License

MIT
