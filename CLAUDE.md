# D4Pressure — Claude context

## What this is

A .NET 10 / Avalonia 11 desktop app (Windows only) that auto-presses configurable keys and mouse buttons in a loop while Diablo IV has focus. Topmost overlay, no console window, no game injection.

## Build

```
dotnet build -c Release
```

Run from `bin\Release\net10.0-windows\D4Pressure.exe` — never `dotnet run` (spawns a terminal window).

The csproj has `<OutputType>WinExe</OutputType>` and `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.

## Project layout

```
D4Pressure/
├── Program.cs                     Avalonia bootstrap
├── App.axaml / App.axaml.cs
├── Assets/app.ico                 Generated icon
├── Models/ConfigModel.cs          JSON-serialised per-character config records
├── Services/
│   ├── GlobalHotkey.cs            WH_KEYBOARD_LL hook — F2 toggle, F3 overlay, F9 quit
│   ├── InputSender.cs             Win32 SendInput (unsafe, stackalloc, zero heap alloc)
│   └── ScreenCapture.cs           GDI+ screen region capture for skill icon detection
├── ViewModels/
│   ├── MainViewModel.cs           All app logic — character profiles, input loop, settings
│   └── KeyRowViewModel.cs         Per-row binding (key/mouse, mode, delay, icon)
└── Views/
    ├── MainWindow.axaml/.cs       Main config window
    └── OverlayWindow.axaml/.cs    Minimal topmost overlay (button + animated keys)
```

## Key architecture decisions

**NOACTIVATE**: Both windows carry `WS_EX_NOACTIVATE` so they never steal focus from the game. The style is lifted temporarily when a TextBox or the capture overlay gets focus, then restored.

**Global hook vs RegisterHotKey**: Uses `WH_KEYBOARD_LL` (not `RegisterHotKey`) so hotkeys fire even when the game window is foreground. The hook filters `LLKHF_INJECTED` (0x10 in flags at offset 8 of KBDLLHOOKSTRUCT) to ignore synthetic keystrokes sent by the app itself — critical to prevent F2-bound rows from toggling the loop off.

**Custom drag/resize**: `BeginMoveDrag` fails with NOACTIVATE. The overlay uses `GetCursorPos` P/Invoke with manual `Position` and `Width`/`Height` updates. `DragMode` enum (None/Move/Resize) + `WindowEdge` tracks state.

**Input loop**: One `Task.Run` per enabled Tap row using `CancellationToken`. Hold-mode keys are pressed immediately on start and released on stop. Delay is jittered ±20% (`baseDelay * 0.8` … `baseDelay * 1.2`).

**Character profiles**: Per-character JSON at `%LocalAppData%\D4Pressure\characters\{Name_Class}.json`. App settings (character list, last character, toggle key, window positions) at `%LocalAppData%\D4Pressure\settings.json`.

**Window position persistence**: `MainViewModel` holds `MainWindowX/Y` and `OverlayX/Y` as plain int? properties. `AutoLoad` sets them and raises `PropertyChanged(nameof(MainWindowX))` — MainWindow listens with a one-shot handler to reposition. Overlay position saved on close via `UpdateOverlayPosition`. Positions validated against `Screens.All` before applying.

## LSP false positives

The Roslyn LSP spits hundreds of "Predefined type not defined" errors before `dotnet restore` runs. `dotnet build` is always authoritative. Ignore LSP unless the build fails.

## What not to do

- Do not add `using System.Windows.Forms` — pure Avalonia.
- Do not use `BeginMoveDrag` on the overlay window (breaks with NOACTIVATE).
- Do not remove the `LLKHF_INJECTED` filter from `GlobalHotkey.cs` — it prevents feedback loops when keys are bound as row inputs.
- Do not add animation via inline object setters in AXAML KeyFrame Setters (`<TranslateTransform>` inside `<Setter Value>`) — crashes at runtime. Use scalar property animation (Opacity, Margin) instead.
