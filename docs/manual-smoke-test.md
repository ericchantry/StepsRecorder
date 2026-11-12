# Manual Smoke Test

Use this checklist before demoing OpenSteps.

## Recording Flow

1. Run `dotnet run --project src/OpenSteps.App`.
2. Confirm the compact OpenSteps recorder bar appears.
3. Click **Start Recording**.
4. Confirm the recorder hides, the tray icon appears, and the tray tooltip/status updates with elapsed time and step count.
5. Use the tray menu to show the recorder, then drag the compact recorder bar and confirm it moves.
6. Open Notepad and click inside the editor area.
7. Open File Explorer and click a folder or navigation item.
8. Open Windows Settings and click a visible setting.
9. Pause from the tray menu or recorder bar, click in another app, and confirm no new step is added.
10. Click **Resume**, click in another app, and confirm one new step is added.
11. Click the recorder controls several times and confirm OpenSteps clicks are excluded.
12. If the main OpenSteps window is visible while recording, click it and confirm those clicks are excluded.
13. Stop from the tray menu or recorder bar.

## Taskbar And OpenSteps Click Filtering

1. Start OpenSteps recording.
2. Leave OpenSteps visible or foreground.
3. Click a Notepad icon on the Windows taskbar to open or maximize Notepad.
4. Confirm no new recorded step is created for the taskbar click.
5. Confirm no screenshot of OpenSteps is created.
6. Confirm the taskbar click does not appear as a recorded step.
7. Click inside Notepad.
8. Confirm that click records normally.
9. Open Notepad and File Explorer.
10. Start recording and click the File Explorer taskbar icon.
11. Confirm the taskbar click is skipped and the next click inside File Explorer records normally.
12. Put Chrome or Notepad visible behind OpenSteps.
13. Start recording and click directly inside the background app window, not the taskbar.
14. Confirm the step records the clicked app/window, not OpenSteps.
15. With active-window screenshot mode enabled, confirm the screenshot captures the clicked app.
16. Start recording and click Pause, Resume, or Stop on the OpenSteps recorder controls.
17. Confirm OpenSteps control clicks are skipped and no steps are created from those clicks.
18. If available, repeat the taskbar switch test from a secondary monitor taskbar.

## Editor And Export

1. Confirm the editor opens with one card per recorded click.
2. Expand **Capture debug/status** for each step.
3. Verify click coordinates, screenshot path, active window title, process name, UI Automation element name/control type, success flags, and any error text.
4. Edit a step title and description.
5. Click **Edit screenshot** on step 1.
6. Drag a rectangle over visible text.
7. Click **Save**.
8. Verify the step preview shows a pixelated redaction and a redacted/edited label.
9. Verify the original screenshot still exists locally beside the redacted image.
10. Delete a step.
11. Move a step up or down.
12. Click **Save Session**.
13. Close and reopen the app, then click **Open Previous Session**.
14. Confirm screenshots, edited text, and redactions still load.
15. Click **Export**, choose Markdown and/or HTML, and choose an export folder.
16. Confirm the success message shows the generated `guide.md` and/or `guide.html` paths.
17. Open the exported folder when prompted.
18. Open `guide.md` or `guide.html` and verify relative image links under `images/` work.
19. Verify exported redacted steps use the redacted image.

## Multi-Monitor Capture

1. Connect two monitors.
2. Put Notepad on monitor 1.
3. Put File Explorer or Settings on monitor 2.
4. Start recording with screenshot mode set to **Full desktop**.
5. Click Notepad on monitor 1.
6. Click File Explorer or Settings on monitor 2.
7. Stop recording.
8. Verify the screenshots and debug metadata reflect the clicked windows/monitors.
9. Repeat with screenshot mode set to **Active window only** and verify screenshots capture the clicked app window when possible.
10. Verify the click highlight appears in the correct place for click steps.
11. Test a secondary monitor placed left of the primary if possible.
12. Test different DPI scaling on the monitors if possible.
13. Expand **Capture debug/status** and verify monitor device, bounds, screenshot size, and local click coordinates match the captured image.

## Known Capture Edge Cases

- Some apps expose little or no UI Automation metadata; screenshots and click coordinates should still be captured.
- Full virtual desktop capture is available as a legacy/debug screenshot mode and can create large images.
- High-DPI scaling can affect recorder positioning; verify on scaled displays.
- Elevated/admin apps may block metadata or hook behavior when OpenSteps is not also elevated.
- Full-screen apps may hide the recorder or interfere with global hooks.
- Screenshots can include private data. Review steps before sharing or exporting.

## Video And Input Timeline

1. On the recorder bar, leave **Video** and **Typed text** checked and start recording.
2. In Notepad type `hello wrold`, press Backspace three times, type `rld`, then press Ctrl+S and Escape.
3. Click into a browser's password field and type a few characters.
4. Scroll a long page, drag a window by its title bar, and middle-click a link.
5. Pause for 10 seconds, resume, then stop.
6. In the editor, confirm the text step reads `Type 'hello world' ...` and the password step reads `Enter hidden text`.
7. Click **Video & timeline**. Confirm the video plays, the timeline shows `Typed 'hello world'` with `Keys: hello wrold⌫⌫⌫rld`, `Pressed Ctrl+S`, `Typed hidden text (n keys)`, scroll, drag and middle-click rows.
8. Click a timeline row and confirm the video jumps there; play and confirm the current row highlights. Confirm the 10-second pause is not in the video.
9. Click the time button on a step card and confirm the review window opens at that step.
10. From the tray icon, untick **Record typed text** mid-recording; confirm later typing shows `Typed n keys (text not recorded)`.
11. Export HTML with video and **Also create a .zip**; open `guide.html`, click a timeline row and a step's **Watch at** button.

## Install Scripts (no admin)

1. Run `.\build\Publish-OpenSteps.ps1` and extract the zip as a standard (non-admin) user.
2. Run `install.cmd`. Confirm the Start Menu shortcut, the Settings > Apps entry, and `%LOCALAPPDATA%\Programs\OpenSteps`.
3. Run `install.cmd` again; confirm the log says the version is already installed.
4. Run `uninstall.cmd -Quiet`; confirm the shortcut, Apps entry and install folder are gone and `%LOCALAPPDATA%\OpenSteps\Sessions` is kept.

## Recording Bar

1. Start recording and confirm the Pause / Stop / Cancel bar appears at the bottom of the screen and the main recorder bar hides.
2. Type into Notepad, click the bar's **Pause**, and confirm Notepad still has keyboard focus (the cursor keeps blinking) and the bar shows **Paused**.
3. Confirm the bar does not appear in step screenshots or in the session video.
4. Drag the bar by its grip to another spot and keep recording.
5. Click **Cancel**, choose **No**, and confirm recording continues. Click **Cancel** again, choose **Yes**, and confirm the recorder bar returns and no new guide was saved.
6. Use **Record more steps** on an existing guide, record two clicks, then **Cancel** > **Yes**. Confirm the editor shows only the original steps.
7. Start again and click **Stop**; confirm the editor opens with the new steps.

## Capture Area (multiple monitors)

1. Open the **Capture** list and confirm it shows **All monitors**, one entry per monitor named by position, e.g. **Left monitor · 1920×1200** (the main one marked), and **Active window**.
2. Pick one monitor. Record a click on that monitor, and a click and some typing on another monitor. Stop.
3. Confirm only the click on the chosen monitor became a step, its screenshot shows only that monitor, the timeline has nothing from the other monitor, and the video shows only the chosen monitor.
4. Pick **Active window**. Record clicks in a window on one monitor, then a window on another. Confirm each screenshot shows only the clicked window and the video switches to the second monitor when focus moves there.
5. Unplug or disable the chosen monitor (or change display settings), reopen the Capture list, and confirm it falls back to the main monitor.

## Single Window

1. Open Excel (or Notepad) and another app. Open the **Capture** list and confirm open windows appear as **Window · <title>**, most recently used first, and that OpenSteps' own windows are not listed.
2. Choose the Excel window. Confirm it is briefly outlined in blue when recording starts.
3. Record a few clicks and some typing in Excel, move and resize the window, open a right-click menu, then click into another app.
4. Stop. Confirm every screenshot shows only the Excel window (including the step from the other app), and the video follows the window as it moves.
5. Minimize Excel while recording; confirm the video holds the last frame instead of showing what's behind it.
6. Choose **Pick a window by clicking it…**, click a window's title bar, and confirm the banner disappears, the window is outlined and it is selected in the list. Try again and click **Cancel**; confirm the previous choice stays.
7. Close the chosen window and press Record; confirm OpenSteps says the window is no longer open and does not start.
8. Restart OpenSteps with the same window still open; confirm it is still selected.

## Screen Region

1. Open the **Capture** list and choose **Select a region…**. Confirm the recorder bar hides, the screen dims with a banner at the top, and the cursor is a crosshair.
2. Press Esc; confirm nothing changes and the previous choice is back in the list. Right-click does the same.
3. Choose it again and drag a rectangle (try dragging up and to the left). Confirm the size label follows the drag, the area is outlined in blue after release, and the list shows **Region · W×H**.
4. Try a tiny drag (a click); confirm it is ignored and you can drag again.
5. Press Record. Confirm a dashed red outline surrounds the region and does not appear in screenshots or the video.
6. Click inside the region, outside it, and type in a window that doesn't overlap it. Stop. Confirm only the click inside became a step, screenshots and video show only the region, and the timeline has nothing from outside it.
7. Choose **Select a new region…** and press Enter; confirm the current region is kept.
8. Draw a region on a second monitor, disconnect that monitor, and press Record; confirm OpenSteps asks for a new region instead of starting.

## Click Markers And Cursor Highlight

1. Record a click, a right-click, a double-click and a middle-click (middle-click only shows in the video).
2. Confirm screenshots show a red **Click**, blue **Right-click** and purple **Double-click** ring with a label, the label stays inside the image for clicks near an edge, and the ring is visible on both light and dark backgrounds.
3. Play the video. Confirm each click shows a growing ring in the same colour with L, R, 2× or M, and a yellow halo follows the pointer.
4. Untick **Highlight cursor**, record again, and confirm the halo is gone but click rings remain. Confirm the box can't be changed while recording or with video off.

## Sensitive Field Covering

1. Open a sign-in page (or any app with a password box) and a form with a field labelled e.g. *Card number* or *PIN*.
2. Record clicking into the password box and typing, then into the card-number field and typing.
3. Stop. Confirm both fields are covered by a dark block labelled *Hidden by OpenSteps* in the screenshots and throughout the video, including while typing, and the timeline shows the typed text as masked.
4. Move the window while the password box is focused; confirm the cover moves with it.
5. Confirm ordinary fields (e.g. *Search*, *Name*) are not covered.

## Step Editor

1. Record a few steps and stop. Click the pencil on a step; confirm the **Edit step N** window shows the screenshot, the title and description, and a **Suggested** button when the title differs from OpenSteps' suggestion.
2. Draw a box, an arrow, a highlight and a blur, and place three callouts. Confirm the callouts read 1, 2, 3 and the hint shows the next number.
3. Right-click callout 2; confirm it disappears and the remaining callouts read 1, 2. Right-click the blur; confirm it is removed.
4. Crop to part of the screenshot. Confirm the outside is dimmed, not removed, and **Remove crop** brings the whole image back. Crop again.
5. Press Ctrl+Z a few times and Ctrl+Y; confirm each edit comes and goes. Change the title and description and click **Save**.
6. Confirm the step list shows the cropped, annotated screenshot with an **Edited** badge and the new title and description. Export to HTML and confirm the export uses the edited image.
7. Open the editor again; confirm every edit and the crop are still there and can be changed one by one, and that the full original is shown underneath.
8. Click **Reset to original** and **Save**; confirm the step shows the original capture again and the badge is gone. Check the session's `images` folder: the original `step-NNN.png` is unchanged throughout.
9. Make a change and close the window with the X; confirm OpenSteps asks whether to save. Cancel keeps the window open; No discards.
10. Open a session edited with an earlier version that has boxes or arrows; confirm a notice explains they can't be changed one by one, saving only text keeps the old image, and changing the screenshot starts from the original.

## Step Titles

1. Record: clicking a button, a menu bar item and an item in its menu, a tab, a list item, a checkbox, a text field, typing into a search box, a password box, Ctrl+C, Ctrl+Shift+P and Alt+Tab, and a click on an empty area of Excel or Notepad.
2. Confirm titles read like `Click "Save"`, `Open the "File" menu`, `Choose "Save As"`, `Open the "Home" tab`, `Select "Documents"`, `Click the "Remember me" checkbox`, `Click in "Search"`, `Type "invoice 1042" in "Search"`, `Enter the password`, `Press Ctrl+C to copy`, `Press Ctrl+Shift+P`, `Press Alt+Tab to switch windows`, and `Click in Excel`.
3. Confirm long element names are shortened with "…" and no title contains a keyboard-shortcut hint such as "(Ctrl+S)".


## Keyboard Recording

1. Start a recording with **Keyboard** ticked, type into Notepad, stop, and confirm the typed text and shortcuts appear on the timeline.
2. Untick **Keyboard**, record again and type. Confirm nothing typed appears: no characters, no key presses and no shortcuts.
3. While recording with **Keyboard** on, press Pause and type. Confirm nothing typed is recorded for that stretch, then Resume and confirm typing is recorded again.
4. Set the `DisableTypedTextCapture` policy, start OpenSteps and confirm the **Keyboard** box is greyed out and nothing typed is recorded.

## Step Editor: Select, Text And Colours

1. Open a step with a box, an arrow, a callout and a blur. Confirm the editor opens on the **Select** tool.
2. Click the box. Confirm a dashed outline with four square handles. Drag the middle to move it, then a corner to resize it.
3. Drag one end of the arrow; confirm only that end moves. Move the callout and the blur the same way.
4. With the box selected, click the purple swatch and confirm the box turns purple. Click a swatch with nothing selected, draw a new box, and confirm it uses that colour.
5. Press Delete with an edit selected; confirm it is removed and callouts renumber. Press Escape to deselect.
6. Choose **Text**, click on the screenshot, type a label and confirm it appears. Double-click it with Select to change the words. Try it in yellow and confirm the text is dark enough to read.
7. Undo and redo the whole sequence; confirm each move and resize is one step.
8. Save, reopen the step and confirm every edit is where you left it.

## Step List: Select, Merge, Duplicate

1. Tick two steps. Confirm the bar appears with the count, and **Merge into one step** is enabled only with two or more ticked.
2. Click **Merge**; confirm the first step keeps its screenshot and title and gains the other's title and description, and the rest renumber.
3. Click **Duplicate** on a step. Confirm the copy appears below it. Edit the copy's screenshot and confirm the original is untouched.
4. Tick several steps and click **Delete**; confirm it asks first and removes them all.

## Crash Recovery And Single Instance

1. Start recording, capture a few steps, then end the process from Task Manager.
2. Start OpenSteps again; confirm it offers the recovered guide and the steps are there. If a video run was interrupted, confirm the review window explains the video could not be finished.
3. Start OpenSteps while it is already running; confirm the running window comes to the front and no second copy appears.

## Themes And Screen Readers

1. Click the theme button on the recorder bar through Match Windows, Light and Dark. Confirm every open window changes at once, including title bars, and the choice survives a restart.
2. Set Windows to a high-contrast theme; confirm OpenSteps follows it while it is on, whatever the button says.
3. Turn on Narrator and tab through the recorder bar, session editor and step editor. Confirm each button is announced with a useful name rather than a symbol.

## Installer During A Recording

1. Start a recording. Run `Install-OpenSteps.ps1` and confirm it exits with code `2` and logs that the upgrade was postponed.
2. Run it with `-InterruptRecording` and confirm OpenSteps stops and saves the recording, closes, and the upgrade completes with the guide intact.
3. With OpenSteps open on the session editor (recorder bar hidden), run the installer and confirm it closes cleanly without being force-stopped.
