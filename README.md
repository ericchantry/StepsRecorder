# OpenSteps

> [!WARNING]
> OpenSteps is an early Windows beta. It is usable for day-to-day recording, but review every screenshot and video before you share a guide.

<p align="center"><strong>A local Windows recorder that turns what you do into step-by-step guides and exact repro steps — with screenshots, a full video and a keystroke timeline.</strong></p>

OpenSteps is a modern replacement for Windows Steps Recorder. Press Record, do the task, press Stop, and you get an editable guide: one step per click or key press, each with a screenshot, plus a video of the whole session and a timestamped log of every click, key, shortcut and the text you typed. Export it as Markdown or HTML, or zip it up and attach it to a ticket.

Everything stays on your computer. No account, no cloud, no telemetry, and no administrator rights needed to install or run it.

<p align="center">
  <img src="img/initial.png" alt="The OpenSteps recorder bar" width="720" />
  <br />
  <em>The recorder bar</em>
</p>

<p align="center">
  <img src="img/guide.png" alt="The OpenSteps guide editor" width="720" />
  <br />
  <em>Editing a captured guide</em>
</p>

## Who it's for

- **Process documentation** – SOPs, onboarding notes, training material and IT how-tos.
- **Bug reports and QA** – exact steps to reproduce an issue, with the video and every keystroke to prove it.
- **Support** – customer and internal walkthroughs.

## What's new in 0.2

- **Session video.** A full MP4 of all your monitors, with the cursor and a highlight on every click. Paused time is cut out.
- **Keystroke and mouse timeline.** Every key and shortcut (Ctrl+F, Alt+Tab, F5, Win+R…), the text you typed, clicks, scrolls, drags and middle clicks — each with a timestamp and the window it went to.
- **Video & timeline review.** Watch the video next to the timeline. Click any row to jump to that moment; the current row highlights as it plays. Every step has a button that opens the video at that step.
- **Typed text in steps.** Steps now read like `Type 'invoice 1042' into the 'Search' field`. Backspace corrections are applied.
- **Password masking.** Password fields, and fields that look like secrets (PIN, CVV, card number, security code…), are always masked and never stored.
- **On-screen Pause / Stop / Cancel bar** while recording. It never takes focus from the app you're recording and never appears in screenshots or the video. Cancel throws away everything since you pressed Record.
- **Bug-report details.** Windows version and build, displays and scaling, language, keyboard layout and time zone — never user or computer names.
- **Richer exports.** HTML embeds the video with a clickable timeline and "Watch at 0:12" links on each step. Markdown gets the input log. Optional single .zip for tickets.
- **IT deployment without admin rights.** A per-user install script for Intune, Configuration Manager or a logon script, plus policies to turn typed-text capture or video off. See [docs/deployment.md](docs/deployment.md).
- Shortcuts are no longer limited to a handful of keys, and the recorder bar now shows the screenshot mode by name.

## Install

Download `OpenSteps-<version>-win-x64.zip` from [Releases](https://github.com/ericchantry/StepsRecorder/releases), extract it, and double-click **`install.cmd`**.

- Installs for the current user only, to `%LOCALAPPDATA%\Programs\OpenSteps`. **No administrator rights needed.**
- Adds a Start Menu shortcut and an entry in **Settings > Apps** (uninstall from there or with `uninstall.cmd`).
- Self-contained: no .NET runtime to install. You can also run `OpenSteps.App.exe` straight from the extracted folder.
- Requires Windows 10 version 1809 or later, x64.

**For IT departments:** `install.cmd -Quiet` installs silently from the package, a file share or GitHub, and is safe to run at every logon. Typed-text capture and video can be disabled by policy. The [deployment guide](docs/deployment.md) covers Intune, Configuration Manager, Group Policy, policies, and what to plan for (code signing, AppLocker, execution policy).

OpenSteps is not code-signed yet. SmartScreen may warn on first launch, and security software may flag the keyboard hook.

## Using it

1. **Choose what to capture** on the recorder bar:
   - **Capture** – *Full Desktop* (all monitors) or *Active Window* screenshots.
   - **Video** – record a video of the session.
   - **Typed text** – record the characters you type (password fields are always masked).
2. **Press Record** (●). The recorder bar hides and a small bar appears at the bottom of the screen:
   - **Pause / Resume** – nothing is recorded while paused, and the pause is cut from the video.
   - **Stop** – finish and open the guide editor.
   - **Cancel** – discard this recording (asks first).

   The tray icon has the same controls, plus **Record typed text** so you can switch it off before typing something private.
3. **Do the task.** Each click, double-click, right-click, Enter/Tab/Escape, function key and shortcut becomes a step with a screenshot. Typing is grouped into one step per field.
4. **Edit the guide.** Rename it, edit step titles and descriptions, reorder, delete or insert steps, and edit screenshots (crop, redact, annotate with boxes, arrows, highlights and numbered markers, or replace by importing or pasting). Click **Video & timeline** to review the recording.
5. **Export** as Markdown and/or HTML, optionally with the video, the input timeline, system details and a .zip.

## Export

An export is a portable folder that does not link back to your local session:

```text
My Guide/
  guide.md
  guide.html
  images/
    step-001.png
    step-002.png
  videos/
    recording-1.mp4
```

- **HTML** – opens in any browser. Includes the video player beside a clickable timeline that highlights as the video plays, a "Watch at" link on each step, and a system details table.
- **Markdown** – portable and version-controllable. Includes the input timeline as a table and links to the video.
- **.zip** – the whole folder in one file, for attaching to a ticket or email.

Video, the input timeline and system details can each be left out.

## Privacy

OpenSteps only records while you have pressed Record, and shows the recording bar and a tray icon the whole time. Recordings stay on your computer in:

```text
%LOCALAPPDATA%\OpenSteps\Sessions
```

Each session holds a `session.json` (steps, input timeline, system details), its screenshots and its videos. You can reopen, rename, edit, export or delete sessions.

- **Typed text** is on by default so repro steps show exactly what was entered. Characters typed into password fields, or fields named like PIN, CVV, card number or security code, are never stored. Turn **Typed text** off on the recorder bar or from the tray to record only key names, shortcuts and key counts. IT can force it off.
- **The video records everything visible on screen**, including text as it appears in non-password fields. Pause before showing anything private, and review the video before you share an export.
- **Screenshots** can contain private data too. Use crop and redaction, or Active Window mode, and review every step.
- **System details** never include your user name, computer name or network details.

## Build from source

Requirements: Windows 10 or later and the .NET 8 SDK. No admin needed — `dotnet-install.ps1 -Channel 8.0 -InstallDir $env:LOCALAPPDATA\Microsoft\dotnet` installs it into your profile.

```powershell
dotnet build OpenSteps.sln
dotnet run --project src\OpenSteps.App\OpenSteps.App.csproj
dotnet test OpenSteps.sln
```

The real screen-recording test only runs in an interactive session with `OPENSTEPS_RUN_SCREEN_TESTS=1` set.

Build the release package (self-contained zip with the install scripts, plus its SHA-256):

```powershell
.\build\Publish-OpenSteps.ps1
```

Pushing a version tag (for example `v0.2.1`) runs the GitHub Actions release workflow, which builds, tests and publishes the zip to Releases.

Project layout:

| Folder | What's in it |
|---|---|
| `src/OpenSteps.Core` | Models, timeline, title generation, export, storage |
| `src/OpenSteps.Capture` | Keyboard/mouse hooks, UI Automation, screenshots, video recorder |
| `src/OpenSteps.App` | WPF app: recorder bar, recording bar, editor, review window |
| `tests/OpenSteps.Tests` | xUnit tests |
| `deploy/` | Per-user install and uninstall scripts |
| `build/` | Release packaging script |
| `docs/` | Deployment guide and manual smoke test |

## Known limits

- Without admin rights, Windows does not let OpenSteps see clicks or keys in apps running as administrator.
- The UAC prompt and lock screen can't be captured; the video shows the last visible frame.
- Typed text is rebuilt from key presses. Backspace is applied, but edits made by moving the cursor, accents typed with dead keys, and IME input may not come out exactly; the raw key sequence is kept on the timeline.
- The video covers all monitors in one frame, scaled down to fit 3840×2160 on very wide desktops. Screenshot crop and redaction don't apply to the video.
- Some apps expose little UI Automation information, so their step titles fall back to the window name.
- Active-window screenshots fall back to full desktop when Windows can't report usable window bounds.
- Full-screen and DRM-protected content may not be captured.
- Windows only.

## Roadmap

- Record a single monitor or window in the video.
- Redaction for video.
- More reliable capture around unusual shell surfaces.
- Better generated titles from UI Automation metadata.
- Richer HTML export styling.

## Contributing

See [AGENTS.md](AGENTS.md) for repository guidelines. Keep changes focused, add tests for core behavior, and include manual verification notes for capture or WPF UI changes ([docs/manual-smoke-test.md](docs/manual-smoke-test.md)).

## License

MIT. See [LICENSE](LICENSE).
