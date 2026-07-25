## Accessible Live Captions 0.4.0

Live captions for anything your PC can hear — built so that a screen-reader user can
actually **read back** what was said, not just glimpse it.

Windows has Live Captions, but it shows a sliver of text that vanishes, keeps no
history you can review, and is effectively invisible to Narrator, NVDA, and JAWS. This
app captions the same audio — a meeting, a video, one specific application, or your
microphone — entirely on your own machine, and puts every finished line into a full,
scrollable transcript where each line is its own focusable item you can arrow through
at your own pace.

Everything runs **on-device**. No cloud, no account, no API key, and no audio ever
leaves your PC.

### What you need

A **Windows on ARM (Snapdragon) PC running Windows 11**. This is a demonstration build
and is ARM64-only — it will not run on an Intel or AMD PC.

### Which download?

| File | What you get |
| --- | --- |
| **`.msix`** | The full app, including the Windows on-device (NPU) engine. Double-click to install — it is code-signed, so no Developer Mode and no certificate steps. |
| **`-portable.zip`** | Unzip and run; nothing is installed. Handy from a USB stick or on a machine where you can't install software. Does not include the NPU engine, which Windows grants only to installed apps. |

### Two ways to see captions

Press **F7**, or use **View ▸ Presentation**, to switch at any time.

- **Transcript** — the full scrollable history. Arrow keys, `Home` and `End` move
  through it line by line; new captions never steal focus while you are reading.
- **Panel** — one caption at a time in large text, the way the built-in Windows Live
  Captions presents itself.

Choosing the compact panel costs you nothing. Every caption is still kept, so switching
back to Transcript shows the whole session from the first line, and captions are
announced to your screen reader identically in both. In panel mode, `↑` and `↓` step
back through earlier captions and pause following so an arriving caption can't pull you
off the line you're reading; `End` or **Follow live** resumes. The readout tells you
which state you're in — "Live — 24 captions" or "Caption 6 of 24 — paused".

### Four speech engines

Choose under **Audio ▸ Engine**. All of them run on your machine.

| Engine | Words appear | Text style | Notes |
| --- | --- | --- | --- |
| **Whisper** (default) | After each phrase | Punctuated | Model picker from Tiny to Large v3; downloads on first use |
| **Streaming** | Word by word, ~½ s behind | Lowercase, no punctuation | Lowest delay and very light on the CPU; 456 MB model on first use |
| **Windows on-device (NPU)** | After each phrase, fast | Punctuated | The recognizer Windows itself uses. Runs on the neural processor, so it costs almost no CPU or battery. English only; `.msix` download only |
| **Windows speech** | Instant | Plain | No download, but microphone only |

### What you can caption

Your **microphone**, **everything the PC is playing**, or **one specific application**
(useful for captioning a meeting without music or notifications bleeding in).

If you use a screen reader, **Audio ▸ Exclude screen reader speech** — on by default —
keeps NVDA, JAWS, or Narrator out of the captions, so a transcript of a meeting isn't
interleaved with your own screen reader's narration.

### Reading and keeping captions

- Announcements of new captions can be toggled with **F8**.
- Caption text scales from 14 to 48 point with `Ctrl +` and `Ctrl −`, and the app uses
  system colors so High Contrast themes apply automatically.
- Optional timestamps, **Copy all** (`Ctrl+Shift+C`), and **Save transcript** (`Ctrl+S`).
- `Tab` deliberately does nothing anywhere in the app — the transcript is the content,
  and every command lives on the menus, reached with `Alt` or `F10`.

### Installing without the mouse

```powershell
Add-AppxPackage -Path .\AccessibleLiveCaptions-v0.4.0-win-arm64.msix
```

If the App Installer dialog sits at **0%** with no error, the problem is Windows'
package-deployment queue rather than this download — a reboot clears it. A healthy
install of this package takes under a second, since it has no downloadable
dependencies, so any wait beyond a few seconds means something else is holding the
queue. The
[project status document](https://github.com/kellylford/LiveCaptionsWithAccessibility/blob/main/docs/ProjectStatus.md)
explains how to confirm that from the event log.

### Known limitations

- **ARM64 only.** There is no Intel or AMD build.
- The NPU engine uses a **preview Windows API**: English only, and a future Windows
  update could change its behavior. The other three engines are unaffected.
- Per-application capture and screen-reader exclusion don't work on audio devices that
  don't support per-process capture. The app detects this within a few seconds, tells
  you, and keeps captioning everything rather than silently going quiet.
- Windows SmartScreen may warn on first download until the release builds up
  reputation. Choose **More info**, then **Run anyway**.
- The first start of the Whisper or Streaming engine downloads its model; the status
  line reports progress.

Full documentation is in the
[user guide](https://github.com/kellylford/LiveCaptionsWithAccessibility/blob/main/docs/UserGuide.md).
