First release of the PCR Graphics Controller: an on-air graphics control panel for CasparCG Server (AMCP).

**Download:** `PcrGraphics-v1.0.0-win-x64.zip`, one self-contained `PcrGraphics.App.exe` (Windows 10/11 x64, no .NET install needed) and the two HTML templates.

### Setup
1. Copy `templates\*.html` into CasparCG's `template` folder (tested with CasparCG Server 2.5.1).
2. Start CasparCG, then `PcrGraphics.App.exe` (or `PcrGraphics.App.exe <host> <port>`).

### In this release
- Lower third (layer 1-30) and breaking news (layer 1-31), Arabic RTL, both on air together without overlap
- Cue / Take in / Update / Take out / Clear with F1–F4, PANIC with F12 (clears only this app's layers)
- Tally that changes only on a confirmed CasparCG reply; UNKNOWN when a command may not have run
- Re-sync with CasparCG (`INFO`) on every connect
- Live WebView2 preview of the real template
- Rundown with NEXT (F5), save/open, autosave
- Embedded Cairo font: works on a playout machine without internet

See the README and docs/DEVELOPMENT_LOG.md in the repository.
