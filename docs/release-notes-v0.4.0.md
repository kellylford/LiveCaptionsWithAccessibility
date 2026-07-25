## Accessible Live Captions 0.4.0

Adds a **panel presentation** — the compact, one-line-at-a-time surface that the
built-in Windows Live Captions uses — without giving up the reviewable transcript that
makes this app worth having. The two are the same captions, and you can switch between
them at any time.

**Which download do I want?**

- **`.msix`** — the full app, including the Windows on-device (NPU) engine.
  Double-click to install; it is code-signed, so no Developer Mode and no
  certificate steps.
- **`-portable.zip`** — no installation, no NPU engine. Unzip and run. Useful from a
  USB stick or on a machine where you can't install software.

Both require a **Windows on ARM (Snapdragon) PC running Windows 11**. This is a
demonstration build and is ARM64-only — it will not run on an Intel or AMD PC.

### New: panel mode

**View ▸ Presentation**, or press **F7** to switch.

**Panel** shows one caption at a time in large text and hides the transcript, matching
how the built-in Live Captions presents itself. **Transcript** is the full, scrollable,
arrow-navigable history.

The point of having both is that nothing is lost by choosing the compact one:

- Every caption is still kept. Switch back to Transcript and the entire session is
  there, from the first line.
- Finalized captions are still announced to your screen reader, through exactly the
  same UI Automation notifications the transcript uses. Panel mode sounds identical.
- The panel follows the newest caption on its own, so it keeps up with the audio.

**Reading back in panel mode.** `↑` and `↓` (or `Alt+Up` / `Alt+Down`, or the
**Previous** / **Next** buttons) step through earlier captions; `Home` goes to the
oldest. Stepping back **pauses following**, so an arriving caption cannot yank you off
the line you are reading — the same protection the transcript has always had. Reaching
the newest line again, or pressing `End` or **Follow live**, resumes following. The
position readout says either "Live — 24 captions" or "Caption 6 of 24 — paused".

The Previous / Next / Follow live buttons are clickable and exposed to assistive
technology, but they are deliberately not in the Tab order and never take focus, so
`Tab` still does nothing anywhere in the app.

### Accessibility fixes

- The Presentation menu items respond to **UI Automation toggle requests**, not just
  mouse and keyboard activation. Automation clients — notably **Windows Voice Access** —
  change checkable menu items through the UIA Toggle pattern, which does not raise a
  click; a menu item wired only to clicks would show a checkmark while the setting
  never actually changed.
- The user guide's punctuation was mangled by a bad text encoding (em dashes and menu
  arrows appeared as `â€"`). Fixed; it now reads correctly on GitHub and in screen
  readers.

### Installing without the mouse

```powershell
Add-AppxPackage -Path .\AccessibleLiveCaptions-v0.4.0-win-arm64.msix
```

If the App Installer dialog sits at **0%** with no error, the problem is Windows'
package-deployment queue, not this download — a reboot clears it. A healthy install of
this package takes **under a second**, because it has no downloadable dependencies, so
any wait beyond a few seconds means something else is holding the queue. See the
troubleshooting section of the
[project status document](https://github.com/kellylford/LiveCaptionsWithAccessibility/blob/main/docs/ProjectStatus.md)
for how to confirm it from the event log.

### Everything from 0.3.0 is unchanged

Four on-device engines (Whisper, Streaming, Windows on-device NPU, Windows speech),
microphone or system-audio capture, per-application capture, and screen-reader
exclusion all behave as before. Built on .NET 10 LTS; both downloads signed with Azure
Artifact Signing.

### Known limitations

- ARM64 only; there is no Intel/AMD build.
- The NPU engine uses a preview Windows API. It is English-only, and a future Windows
  update could change its behavior. The other engines are unaffected.
- Per-application capture and screen-reader exclusion do not work on audio devices
  that do not support per-process capture; the app tells you when it hits one.
- Windows SmartScreen may still warn on first download until the release accumulates
  reputation. Choose **More info**, then **Run anyway**.

Full documentation is in the
[user guide](https://github.com/kellylford/LiveCaptionsWithAccessibility/blob/main/docs/UserGuide.md).
