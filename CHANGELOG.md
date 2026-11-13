# Changelog

All notable changes to OpenSteps. Versions follow [semantic versioning](https://semver.org/); each release's zip is on the [Releases](https://github.com/ericchantry/StepsRecorder/releases) page.

## [0.6.0] - 2026-11-12

### Added
- **OpenSteps updates itself.** No more uninstalling and reinstalling to move to a new version.
  - Once a day it looks at its own [releases page](https://github.com/ericchantry/StepsRecorder/releases) and, when a newer version is published, shows what changed between the version you have and the newest one — every release in between, not just the last one.
  - **Update now** downloads the release, checks it against the SHA-256 published with it, and hands it to the same per-user installer IT uses. OpenSteps saves your work, closes, is replaced and starts again. No administrator rights are needed.
  - **Later** asks again tomorrow; **Skip this version** never mentions that one again but still offers the next.
  - A button next to the theme button checks on demand and turns blue when a version is waiting. **Check for updates daily** on the update window turns the daily look on and off.
- The update check never runs, and no window appears, while a recording is in progress.
- If an update does not finish installing, the next start says so and points at `%LOCALAPPDATA%\OpenSteps\install.log`. Nothing is lost: a failed upgrade leaves the version you had in place.
- `DisableUpdateChecks` policy, so an IT department that deploys OpenSteps centrally can switch off both the daily check and the user's ability to update.

### Notes
- Only releases that ship the `OpenSteps-<version>-win-x64.zip` package are offered, and pre-releases are left out unless `IncludePrereleaseUpdates` is set in `settings.json`.
- A copy run from a build folder rather than an install will not replace its own files; it points at the releases page instead.
- The check reads one public GitHub page. Nothing about you, your machine or your guides is sent.

## [0.5.0] - 2026-11-12

### Changed
- **The keyboard hook is installed only when it is needed.** The **Keyboard** option (renamed from *Typed text*) now decides whether OpenSteps listens to the keyboard at all.
  - The hook exists only while a recording is running with **Keyboard** on.
  - It is removed when you pause, stop, switch **Keyboard** off, or when IT sets the `DisableTypedTextCapture` policy.
  - With **Keyboard** off, shortcuts and key presses are no longer recorded either. Before, only the typed characters were left out.
- The muted text colour is darker in the light theme so small labels are easier to read.

### Added
- **Step editor**
  - **Select** tool: click an edit to select it, drag to move it, drag a corner (or an arrow's end) to resize it, press Delete to remove it. Blur areas can be moved and resized too.
  - **Text** tool: click to add a text label. Double-click a label with Select to change its words.
  - **Colours**: red, orange, yellow, green, blue, purple and black for boxes, arrows, callouts and text. Choosing a colour with an edit selected recolours it.
- **Step list**
  - Tick boxes on each step, with a bar to **delete** the ticked steps or **merge** them into the first one (it keeps its screenshot and title and gains the others' text).
  - **Duplicate** a step. The copy has its own images, so editing one never changes the other.
- **Crash recovery.** While recording, the guide is saved every five seconds. If OpenSteps closes unexpectedly, the next start offers to open what was saved. A crash log is written to `%LOCALAPPDATA%\OpenSteps\logs`.
- **One running copy.** Starting OpenSteps a second time brings the running copy to the front.
- **Themes.** Dark and Windows high-contrast themes, following Windows by default, with a button on the recorder bar to choose Match Windows, Light or Dark. Title bars follow the theme.
- **Accessibility.** Every button and field has a name for screen readers; icon-only buttons take it from their tooltip.
- **Guide library.** Search by title or tag (`#tag` matches tags only), add and edit tags, rename a guide from its card, and click a tag to filter by it.
- **Installer**
  - Upgrades ask OpenSteps to save and close in any state, not only when its main bar is showing.
  - An upgrade is postponed (exit code `2`) while a recording is in progress. `-InterruptRecording` stops and saves the recording first instead.

### Notes
- Splitting one step into two is not included; duplicate the step and edit each copy.

## [0.4.0] - 2026-11-04

### Added
- **Step editor.** The pencil on each step opens one window for the whole step:
  - **Crop** – drag the area to keep. The rest is dimmed rather than deleted; **Remove crop** brings it back.
  - **Numbered callouts** – click to place 1, 2, 3… Removing one renumbers the rest.
  - **Box**, **Arrow**, **Highlight**, **Blur** and **Circle**.
  - **Title and description** beside the screenshot, with a one-click **Suggested** title.
  - Right-click an edit to remove it, Ctrl+Z / Ctrl+Y to undo and redo, Ctrl+S to save, and **Reset to original** to remove everything. Closing with unsaved changes asks first.
- An **Edited** badge on steps whose screenshot has edits.
- Lines and callouts are sized for the screenshot, so they stay visible on 4K captures.

### Changed
- **The original capture is never changed.** Edits are stored with the step and drawn onto a copy, so a step can be reopened and any edit changed or removed later. Before this, boxes and arrows weren't kept, reopening the editor drew on the already-edited image, and cropping replaced the original screenshot.
- **Shorter, plainer step titles:**
  - `Click "Save"` instead of `Click the 'Save' button`.
  - `Type "invoice 1042" in "Search"` instead of `Type 'invoice 1042' into the 'Search' field`.
  - `Click in Excel` instead of the full window title.
  - `Click on the screen` instead of screen coordinates.
- Titles use the verb that fits the control: `Open the "Home" tab`, `Select "Documents"`, `Choose "Save As"`, `Open the "Country" list`, `Click the "Remember me" checkbox`.
- Common shortcuts say what they do: `Press Ctrl+S to save`, `Press Alt+Tab to switch windows`, `Press Win+Shift+S to take a screenshot`.
- Labels lose keyboard-shortcut hints such as "(Ctrl+S)" and trailing "…", and labels over 40 characters are shortened at a word.
- The separate crop window has been folded into the step editor.

### Notes
- Steps edited with an earlier version keep their edited image until the screenshot is changed in the new editor, which then starts again from the original capture. The editor shows a notice for these steps.

## [0.3.0] - 2026-11-04

### Added
- **Screen region capture.** **Select a region…** in the Capture list lets you drag a rectangle over the screen.
  - Screenshots and video show only that area.
  - Clicks outside it, and typing in windows that don't overlap it, are ignored.
  - A dashed outline, which doesn't appear in the recording, marks the region while you record.
- **Click markers that say what happened.** Screenshots mark each click with a coloured ring and label: red Click, blue Right-click, purple Double-click. The video shows matching animated rings, tagged L, R, 2× or M.
- **Cursor highlight.** A yellow halo follows the pointer in the video. Turn it off with **Highlight cursor** on the recorder bar.
- **Password and sensitive fields are covered as they are captured.** Password fields, and fields named like PIN, CVV, card number, SSN or security code, are blacked out in screenshots and in every video frame before anything is saved. OpenSteps reads where these fields are, never what is in them.

## [0.2.1] - 2026-10-30

### Added
- **On-screen Pause / Stop / Cancel bar** while recording. It never takes focus and never appears in screenshots or the video. Cancel discards everything since you pressed Record.
- **Record a single monitor.** Monitors are named by position (Left / Middle / Right monitor), and activity on the others is left out of the screenshots, video and timeline.
- **Record a single window.** Pick an open window from the list or by clicking it. Screenshots and video show only that window, following it as it moves.

### Fixed
- **Active window** mode no longer records every monitor in the video. It now follows the monitor the active window is on.

## [0.2.0] - 2026-10-30

### Added
- **Session video.** A native MP4 synced to the steps, with the cursor and a highlight on every click. Paused time is cut out.
- **Keystroke and mouse timeline.** Keys, shortcuts, typed text, clicks, scrolls and drags, each with a timestamp and the window it went to.
- **Video & timeline review.** Click a row to jump to that moment in the video.
- **Typed text in steps**, with password and secret-looking fields always masked and never stored.
- **Bug-report details:** Windows version and build, displays and scaling, language, keyboard layout and time zone.
- **Richer exports.** HTML embeds the video with a clickable timeline, Markdown gets the input log, and there is an optional single .zip.
- **IT deployment without admin rights:**
  - A per-user install script for Intune, Configuration Manager or a logon script.
  - Policies to turn typed-text capture or video off.
  - See [docs/deployment.md](docs/deployment.md).

[0.5.0]: https://github.com/ericchantry/StepsRecorder/compare/382feb1...main
[0.4.0]: https://github.com/ericchantry/StepsRecorder/commit/382feb1
[0.3.0]: https://github.com/ericchantry/StepsRecorder/compare/729dc32...7bfb116
[0.2.1]: https://github.com/ericchantry/StepsRecorder/compare/729dc32...7634a85
[0.2.0]: https://github.com/ericchantry/StepsRecorder/commit/729dc32
