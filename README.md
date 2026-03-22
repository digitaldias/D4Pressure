# D4Pressure

A small Windows desktop app for automating key and mouse button presses in Diablo IV. Runs as a topmost overlay that never steals focus from the game.

## Features

- **Per-key rows** — bind any key or mouse button, set mode (Hold or Tap), delay, and label
- **Jittered timing** — each Tap fires at delay ±20% to avoid obvious bot patterns
- **Character profiles** — separate config per character, auto-loaded on startup
- **Minimal overlay** — press F3 for a small borderless window you can drag anywhere on screen; animated button shows when the loop is running
- **Bindable toggle key** — assign any key as a second start/stop trigger in addition to F2
- **Window position memory** — both windows remember where you left them, per display

## Hotkeys

| Key | Action |
|-----|--------|
| F2 | Start / stop the input loop |
| F3 | Toggle the compact overlay window |
| F9 | Quit |
| Custom | Optional extra toggle key (bind in the hotkeys card) |

Hotkeys fire globally — the game window does not need to lose focus.

## Requirements

- Windows 10 or 11
- .NET 10 runtime

## Building

```
dotnet build -c Release
```

Run `bin\Release\net10.0-windows\D4Pressure.exe`.

## How it works

- **Hold** rows press the key immediately on start and release it on stop.
- **Tap** rows loop: send the key, wait `delay ±20%`, repeat — until stopped.
- The app uses `WH_KEYBOARD_LL` (low-level keyboard hook) so hotkeys reach it even when the game is in the foreground.
- `WS_EX_NOACTIVATE` keeps both windows from ever stealing focus.
- Configs are saved per character to `%LocalAppData%\D4Pressure\`.

## Data files

```
%LocalAppData%\D4Pressure\
├── settings.json                  Character list, last character, toggle key, window positions
└── characters\
    └── {Name_Class}.json          Per-character key rows
```
