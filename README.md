# <img src="Assets/app.png" width="32" height="32" align="middle"> D4Pressure

A lightweight Windows desktop overlay for automating key and mouse button presses in Diablo IV. Runs as a topmost, always-visible window that never steals focus from the game.

> **ToS notice:** Blizzard's EULA broadly prohibits third-party automation software. D4Pressure only sends keystrokes — it does not inject into the game, read memory, or touch game files. Blizzard's enforcement has historically targeted speed hacks, map hacks, and exploits rather than simple key automation tools like this one. That said, no tool is risk-free. Use your own judgement.

## Features

- **Per-key rows** — bind any keyboard key or mouse button, set mode (Hold or Tap), delay, and a label
- **Jittered timing** — each Tap fires at `delay ±20%` to avoid perfectly regular patterns
- **Character profiles** — separate config per character, auto-loaded on startup
- **Compact overlay** — press F3 for a small borderless window you can drag anywhere; an animated button shows when the loop is running
- **Custom toggle key** — assign any key as a secondary start/stop trigger alongside F2
- **Window position memory** — both windows remember their position per display

## Hotkeys

| Key | Action |
|-----|--------|
| F2 | Start / stop the input loop |
| F3 | Toggle the compact overlay |
| F9 | Quit |
| Custom | Optional extra toggle key (set in the hotkeys card) |

Hotkeys are global — the game does not need to lose focus.

## Requirements

- Windows 10 or 11
- [.NET 10 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## Installation

Download the latest release from the [Releases](../../releases) page. No installer — just unzip and run `D4Pressure.exe`.

## Building from source

```
dotnet build -c Release
```

Run `bin\Release\net10.0-windows\D4Pressure.exe`. Do not use `dotnet run` — it spawns a terminal window.

To build a single self-contained executable:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## How it works

- **Hold** rows press the key immediately on start and release it on stop.
- **Tap** rows loop: send the key, wait `delay ±20%`, repeat until stopped.
- Global hotkeys use `WH_KEYBOARD_LL` so they fire even while the game is in the foreground.
- `WS_EX_NOACTIVATE` keeps both windows from stealing focus.

## Data files

Configs are stored in `%LocalAppData%\D4Pressure\`:

```
%LocalAppData%\D4Pressure\
├── settings.json                  Character list, last character, toggle key, window positions
└── characters\
    └── {Name_Class}.json          Per-character key rows
```

## Support

If this saves your wrists, consider buying me a coffee: [ko-fi.com/digitaldias](https://ko-fi.com/digitaldias)

## License

MIT
