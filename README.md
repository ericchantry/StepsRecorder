# OpenSteps

> [!WARNING]
> OpenSteps is an early Windows beta. It is usable for local workflow capture, but you should review every screenshot before sharing or exporting a guide.

<p align="center"><strong>A Windows-native recorder for turning desktop workflows into clean step-by-step guides.</strong></p>

OpenSteps records what you do on Windows, captures screenshots and click context, a full-session video and a timestamped keyboard and mouse timeline, then turns the result into an editable guide you can export as Markdown or HTML. It is built for support docs, onboarding notes, internal SOPs, QA repro steps, and tutorials where you want a local tool instead of a cloud recorder.

<p align="center">
  <img src="img/initial.png" alt="OpenSteps before recording" width="720" />
  <br />
  <em>OpenSteps before recording</em>
</p>

<p align="center">
  <img src="img/guide.png" alt="OpenSteps guide editor" width="720" />
  <br />
  <em>Editing a captured guide</em>
</p>

## What It Does

- Records desktop workflows with global click, right-click, and double-click capture.
- Shows a small Pause / Stop / Cancel bar at the bottom of the screen while recording. It never takes focus from the app you are recording and is hidden from screenshots and the video. Cancel discards everything captured since you pressed Record.
- Records a full-session MP4 video of all monitors (cursor and click rings included) using built-in Windows encoders.
- Records a keystroke and mouse timeline: every key and shortcut (Ctrl+F, Alt+Tab, F5, Win+R...), typed text, scrolls, drags and middle clicks, each with a timestamp and the window it went to.
- Masks typed text in password fields and fields that look like secrets (PIN, CVV, card number...). Typed-text capture can be switched off on the recorder bar, from the tray while recording, or by IT policy.
- Reviews a recording side by side: the video plays next to the timeline, clicking any row jumps to that moment, and every step has a button that opens the video at that step.
- Captures system details for bug reports (Windows version, displays and scaling, language, keyboard layout) without user or computer names.
- Captures screenshots, click highlights, active-window metadata, and UI Automation context.
- Supports full-desktop screenshots or active-window-only screenshots.
- Skips OpenSteps UI, taskbar, and system-tray clicks so recordings stay focused on the real workflow.
- Saves recordings locally as editable sessions.
- Lets you rename guides, edit step titles and descriptions, reorder steps, delete steps, and insert manual steps.
- Lets you capture a screenshot for a manual step.
- Supports screenshot redaction, cropping, annotations, and screenshot replacement before export.
- Collapses long step cards so larger recordings are easier to edit.
- Exports portable Markdown or HTML guides with relative image links. HTML exports embed the video with a clickable, auto-highlighting timeline; both formats include the input log and system details, and the export can be zipped into one file for a ticket.

## Why It Exists

Windows Steps Recorder was useful, but it is dated and limited. OpenSteps is a modern local-first replacement for people who need to document Windows workflows quickly without uploading screenshots, installing a browser extension, or using a SaaS account.

OpenSteps is especially useful for:

- IT support guides.
- Internal process documentation.
- QA reproduction steps.
- Customer support walkthroughs.
- Training and onboarding material.
- Personal notes for repeated desktop tasks.

## Download and install

Download the latest `OpenSteps-*-win-x64.zip` from [GitHub Releases](https://github.com/ericchantry/StepsRecorder/releases), extract it, and double-click `install.cmd`.

The installer is per-user and **does not need administrator rights**. It installs to `%LOCALAPPDATA%\Programs\OpenSteps`, adds a Start Menu shortcut and an entry in Settings > Apps. Run `uninstall.cmd` (or use Settings > Apps) to remove it.

The release is self-contained, so the .NET runtime does not need to be installed separately. You can also just run `OpenSteps.App.exe` from the extracted folder.

IT departments can deploy it silently with Intune, Configuration Manager or a logon script, and can turn typed-text capture or video off by policy. See [docs/deployment.md](docs/deployment.md).

OpenSteps is not code-signed yet. Windows SmartScreen may warn on first launch, and security software may flag the keyboard hook; see the deployment guide.

## Export

OpenSteps exports a portable folder containing the guide file and ordered screenshots:

```text
Exported Guide/
guide.md
guide.html
images/
step-001.png
step-002.png
videos/
recording-1.mp4
```

Markdown and HTML exports use relative links such as:

```markdown
![Step 1](images/step-001.png)
```

The exported guide does not link back to `%LOCALAPPDATA%`, so you can move or share the export folder as a self-contained guide.

## Privacy

OpenSteps runs locally. It does not require an account, does not upload screenshots, and does not include telemetry.

Recordings are saved under:

```text
%LOCALAPPDATA%\OpenSteps\Sessions
```

Each saved session contains a `session.json` file (steps, input timeline, system details), local screenshots and any session videos. You can reopen, rename, edit, delete, and export sessions again.

Screenshot modes affect how much screen content is captured:

- **Full desktop** captures all visible monitors.
- **Active window only** captures the foreground window when possible.

If active-window capture fails, OpenSteps falls back to full-desktop capture so the step is not lost. Screenshots can still contain private data, so review every step before sharing a guide. Use crop and redaction to remove irrelevant or sensitive areas.

The guide editor can import or paste a replacement screenshot when active-window capture grabs the wrong image. Screenshot annotations include rectangles, arrows, semi-transparent highlights, and sequential number or letter markers. Annotation edits are baked into a new PNG copy in the session images folder, so the original captured screenshot is not overwritten.

When **Typed text** is on (the default), OpenSteps stores the characters you type so repro steps show exactly what was entered, for example `Type 'invoice 1042' into the 'Search' field`. Password fields and fields that look like secrets are always masked and their characters are never stored. Turn **Typed text** off on the recorder bar, or from the tray icon mid-recording, to record only key counts, keys and shortcuts. Your IT department can force it off.

The session video records everything visible on screen, including anything you type into visible fields. Pause recording before showing private information, and review the video before sharing an export. Video and screenshots can be left out of an export.

## Build From Source

Requirements:

- Windows 10 or newer.
- .NET 8 SDK with Windows Desktop runtime. (No admin needed: `dotnet-install.ps1 -Channel 8.0 -InstallDir $env:LOCALAPPDATA\Microsoft\dotnet` works.)

Build and run:

```powershell
dotnet build OpenSteps.sln
dotnet run --project src\OpenSteps.App\OpenSteps.App.csproj
```

Run tests:

```powershell
dotnet test OpenSteps.sln
```

The screen-recording test only runs in an interactive session when `OPENSTEPS_RUN_SCREEN_TESTS=1` is set.

Build the release package (self-contained zip with install scripts, plus its SHA-256):

```powershell
.\build\Publish-OpenSteps.ps1
```

## Known Limits

- Elevated/admin apps may block hooks, screenshots, or metadata unless OpenSteps is also elevated. OpenSteps never needs elevation itself.
- The UAC prompt and lock screen cannot be captured; the video holds the last visible frame.
- Typed text is rebuilt from key presses: Backspace is applied, but edits made by moving the cursor, dead keys/accents and IME input may not be reflected exactly. The raw key sequence is kept on the timeline.
- Video records all monitors in one frame; very wide desktops are scaled to fit 3840x2160. The editor's redaction and crop tools do not apply to the video.
- Some apps expose little or no UI Automation metadata; screenshots and click coordinates should still work.
- Active-window capture depends on Windows reporting usable window bounds.
- Full-screen apps and protected content may interfere with screenshot capture.
- Active-window capture may fall back to full-desktop capture when a window cannot be captured safely.
- The app is Windows-first and currently targets local desktop workflows only.

## Roadmap

- More reliable capture around unusual shell surfaces.
- Better generated titles from UI Automation metadata.
- More editor polish for long guides.
- Richer HTML export styling.

## Contributing

See [AGENTS.md](AGENTS.md) for repository guidelines. Keep changes focused, add tests for core behavior, and include manual verification notes for capture or WPF UI changes.

## License

MIT. See [LICENSE](LICENSE).
