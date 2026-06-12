# ScreenTranslate Overlay

Lightweight WPF overlay for in-game text translation using Windows OCR and DeepL.

> The executable and project folder are still named **GameTranslateOverlay** locally; this is the public name on GitHub.
## Borderless windowed required

**Exclusive fullscreen games will not work** with screen capture. Run the game in **borderless windowed** or **windowed** mode so the desktop compositor can capture pixels.

Examples:

- Steam launch options: `-windowed` or `-popupwindow` (game-specific)
- Unreal Engine: `GameUserSettings.ini` → `FullscreenMode=1` (borderless) or `2` (windowed)
- Unity: `-screen-fullscreen 0 -screen-width 1920 -screen-height 1080`
- Emulators: use **windowed** display mode (Dolphin, PCSX2, Ryujinx, etc.)
- RPG Maker / visual novels: prefer windowed or borderless, not exclusive full screen

## Features

- Save capture region (default F9) — no translation until you press translate
- Translate saved region (default F10): capture → OCR → DeepL → HUD overlay
- Drag overlay, copy translation, auto-hide timer with Keep
- System tray: quick source/target language, set region, settings (incl. start with Windows)
- Safe settings load (`config.json`) with corrupt-file backup and defaults merge

## Requirements

- Windows 10/11, .NET 8 SDK
- Language packs for your **source** language with **Optical character recognition** enabled: **Settings → Time & language → Language & region** → add or open your language → **Language options** → **Language features** → install OCR
- **Chinese:** install separate OCR packs for **Chinese (Simplified)** and/or **Chinese (Traditional)** — pick the matching source language in Settings (`ZH-HANS` vs `ZH-HANT`)
- DeepL API key (Free or Pro)

## OCR image prep

Narrow or low-resolution capture regions (common for vertical subtitle strips) are automatically upscaled and contrast-enhanced before OCR. No setting required. If OCR returns empty text, check `%AppData%\GameTranslateOverlay\debug\` for `last_capture.png` and `last_capture_prep.png`.

## Build

```powershell
dotnet build GameTranslateOverlay/GameTranslateOverlay.csproj
dotnet run --project GameTranslateOverlay/GameTranslateOverlay.csproj
```

## Hotkeys (configurable in Settings)

| Default | Action |
|---------|--------|
| F9 | Pick capture region (saved to config) |
| F10 | Translate saved region |


## Logs

Diagnostic logs are written to `%AppData%\GameTranslateOverlay\logs\` (daily files `gto-yyyy-MM-dd.log`). Open the log folder from **Settings → General → Open log folder**.
## Settings

Stored in `%AppData%/GameTranslateOverlay/config.json`.

## License

MIT — see [LICENSE](LICENSE).