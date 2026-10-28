# Development log

How the PCR Graphics Controller was built, phase by phase: what was decided, what broke, and what was learned.
Environment: Windows 11, PowerShell, VS Code, .NET 10 SDK, CasparCG Server 2.5.1 on the same laptop
(channel 1, 1080i5000, screen consumer, AMCP port 5250). No Viz Engine access, so everything targets CasparCG.

## Goal

A control panel an operator can trust live: the panel must never show something different from what is on
air. Every phase was judged against that rule before features.

## Phase 0 – Setup

- Solution `PcrGraphics.slnx`: `PcrGraphics.Core` (net10.0, no UI), `PcrGraphics.App` (WPF, net10.0-windows),
  `PcrGraphics.Core.Tests` (xUnit), `tools/PcrGraphics.Cli`.
- Split from day one: all protocol and state logic in Core, so it can be tested without WPF and without CasparCG.
- **Problem:** the first restore failed because no NuGet source was configured on the machine.
  **Fix:** `dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org`.

## Phase 1 – AMCP client

- `CasparClient`: one TCP connection, commands serialized with a lock, one reply read per command.
- Reply parser by status code: `200` = data lines until an empty line, `201` = one data line, `202` = no data.
- **Decision:** a command with no reply within 2 s closes the connection. A reply that arrives late can then
  never be read as the answer to the next command. `RunAsync` reconnects and sends a `VERSION` heartbeat.
- `FakeCaspar`: an in-process AMCP server for tests (scripted replies, silence, dropped connections).
- CLI test bench (`c`ue, `t`ake, `u`pdate, `o`ut, `p`anic) to try each command against the real server.
- 16 tests.

## Phase 2 – State machine and controller

- States `Empty`, `Cued`, `OnAir`, `Unknown`. Actions Cue (`CG ADD … 0`, loaded hidden), Take in (`CG PLAY`),
  Update (`CG UPDATE`), Take out (`CG STOP`, back to Cued), Clear (`CLEAR layer`).
- **Decision: no optimistic tally.** The state changes only on a 2xx reply. A 4xx keeps the state and shows
  the error. No reply or a lost connection gives `Unknown`, where only Clear is allowed.
- **Busy lock:** while a command waits for its reply, a second press is refused. Clear/PANIC is never refused.

## Phase 3 – WPF control panel

- MVVM with CommunityToolkit.Mvvm: `MainViewModel` (connection, slots, PANIC ALL, log),
  `GraphicSlotViewModel` (one template on one layer: fields, commands, tally).
- Hotkeys F1–F5 and F12. Space takes in only outside a text box; Esc leaves the text box.
- **Decision:** control buttons are `Focusable=False`, so a mouse click never steals focus and the next
  hotkey always acts on the selected graphic.
- `App.xaml.cs` catches unhandled exceptions, writes `crash.log` next to the exe and shows the error
  instead of closing the panel mid-show.
- **Bug:** the app crashed when clicking on a button's text. Walking up the tree from the click with
  `VisualTreeHelper` fails on a `Run`, which is not a Visual.
  **Fix:** fall back to `LogicalTreeHelper` for non-visual elements.

## Phase 4 – Templates and preview

- Second template: breaking news on layer 1-31, independent from the lower third on 1-30.
- **Live preview:** the real HTML template in WebView2, updated while typing, so the operator sees exactly
  what CasparCG will render. Templates are copied to the app output by one csproj item, so they have a
  single source in `templates/`.
- Text that is too long shrinks to fit instead of leaving the screen.
- PANIC ALL clears only this app's layers (1-30, 1-31), never the whole channel.
- **Bug:** the 16:9 preview pushed the editing fields off-screen on smaller windows.
  **Fix:** the preview takes only the space left under the fields and keeps 16:9 inside it.
- **Lesson (build):** files extracted from a zip keep the zip's timestamps, so WPF reused stale generated
  XAML code. Since then `obj` and `bin` of the App are always deleted before building after an update.

## Phase 5 – Rundown

- Items prepared before the show (guest names, headlines), reordered, saved/opened as JSON, autosaved to
  `Documents\PcrGraphics\autosave-rundown.json`. The file is written to a temp file first, then moved, so a
  crash during a save cannot corrupt it.
- **Bug:** F5 ("next") skipped the first item after start-up.
  **Fix:** an explicit pointer and a yellow **NEXT** badge that always shows what F5 will load: the clicked
  item if it is not the one loaded last, otherwise the one after it. If its graphic is on air, F5 only loads
  the text and the item stays NEXT. "+ ADD" does not move the selection.

## Phase 6 – Re-sync with CasparCG after a reconnect

Before this phase, every layer stayed `Unknown` after a reconnect, even when nothing had changed.

**Measure first.** `tools/probe_info.ps1` recorded the raw replies of CasparCG 2.5.1 with the layer empty,
cued, on air, taken out and cleared ([output](casparcg-2.5.1-info-probe.txt)). Findings:

1. `201 INFO OK` is followed by a **multi-line XML document ending with an empty line**, not one line.
   The phase 1 parser would have read one line and left the rest in the stream, mismatching every reply after it.
2. `400 ERROR` is followed by **one line echoing the rejected command**. This was a latent bug from phase 1:
   any 400 would have shifted all following replies by one line.
3. `INFO 1-30` returns the whole channel, the same as `INFO 1`. A cleared layer is absent from the XML.
4. `CG 1-30 INFO` is rejected (400) on 2.5.1.
5. **Cued, on air and taken out give identical XML.** The template hides itself in JavaScript, so CasparCG
   knows *what* is loaded, never *whether it is visible*.

**Built:**

- Reply reader: a 201 whose data starts with `<` is read until the empty line; a 400 consumes its echo line;
  any line that is not a status line is skipped and logged, as a last safety net.
- `ChannelInfo` parses the XML; `LayerSync` sends one `INFO` per channel on every connect.
- `GraphicStateMachine.Reconcile` remembers the last confirmed state when the link drops and whether a
  command was in flight:

  | CasparCG reports | State becomes |
  |---|---|
  | layer empty | Empty |
  | another template or a video | Unknown |
  | our template, link dropped while idle | last confirmed state (Cued / On air) |
  | our template, link dropped during a command | Unknown |
  | our template, panel just started | Unknown |

- `tools/amcp_proxy.py` cuts the AMCP connection without stopping CasparCG, the only way to test the
  restore rule against the real server.
- 25 new tests (79 in total), using the real XML captured by the probe.

## Phase 7 – Split screen (skipped)

Split screen with `MIXER FILL` was dropped: it is not needed for a lower-third / breaking-news panel and
would have doubled the layer logic without making the panel safer.

## Phase 8 – Release

**Single-file publish.** `tools/publish.ps1` runs the tests, publishes one self-contained
`PcrGraphics.App.exe` (win-x64, native libraries bundled, compressed, 62 MB), checks the output and zips it.

- The publish settings only apply when a runtime is given, so normal builds still go to
  `bin\Debug\net10.0-windows`.
- The first publish also produced WebView2 IntelliSense XML files and the Core `.pdb`. They are removed, and
  the script now **fails** if the output contains anything other than the exe and `templates\*.html`.
- WebView2 keeps its cache next to the exe by default, which fails from a read-only folder. The cache and the
  fallback `crash.log` now go to `%LOCALAPPDATA%\PcrGraphics`.

**Templates v2.** A screenshot of the real output showed three problems:

1. The lower third and the breaking-news strap used the same screen zone, so the strap covered the title.
   **Fix:** fixed zones inside 96 px graphics-safe margins (EBU R95): strap at the bottom, lower third above
   it with a 22 px gap. Both can be on air together.
2. The name and title boxes had different widths. **Fix:** one grid, both rows share the widest width.
3. The font came from Google Fonts, so a playout machine without internet fell back to Tahoma.
   **Fix:** Cairo (OFL) subset to Arabic + Latin and embedded as a 45 KB woff2.

While checking the new design in Chromium (the engine CasparCG uses for HTML), the dots under a final
`ي` were cut off in the strap, because the text box hides overflow and the line was too short.
Fixed with a taller line box. Animations were redone: staged entrance, faster reverse exit, and a short swap
for text updated on air instead of a full re-entrance.

**README** with screenshots of the panel and of the CasparCG output taken at the same moment.

## Lessons

- **Measure the real server before coding the parser.** Two of the three reply-format facts found in phase 6
  contradicted assumptions from phase 1.
- **A timeout must close the connection.** It is the simplest way to guarantee that replies stay matched.
- **Tally from confirmed replies only, and an honest `Unknown`.** The operator can always fix Unknown with
  PANIC; a wrong ON AIR cannot be fixed because nobody notices it.
- **Check graphics on the real output, not only in the editor.** The overlap and the clipped dots were only
  visible on a rendered frame.
- **Start CasparCG with its own folder as working directory.** It resolves `template\` and its config
  relative to it; started from elsewhere it does not find the templates.
