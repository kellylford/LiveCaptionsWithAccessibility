## Accessible Live Captions 0.3.0

The first published release of the app in its current form: signed, installable,
four on-device engines, and a transcript built for screen-reader review.

**Which download do I want?**

- **`.msix`** — the full app, including the Windows on-device (NPU) engine.
  Double-click to install; it is code-signed, so no Developer Mode and no
  certificate steps.
- **`-portable.zip`** — no installation, no NPU engine. Unzip and run. Useful from a
  USB stick or on a machine where you can't install software.

Both require a **Windows on ARM (Snapdragon) PC running Windows 11**. This is a
demonstration build and is ARM64-only — it will not run on an Intel or AMD PC.

### Four ways to recognize speech

Choose under **Audio ▸ Engine**.

- **Windows on-device (NPU)** — the same on-device recognizer Windows Live Captions
  uses, reached through the Windows AI Speech API. On a Copilot+ PC it runs on the
  neural processing unit, so captions cost almost no CPU or battery, and the text
  arrives with full punctuation and capitalization. English only. MSIX download only.
- **Streaming** — a true streaming recognizer (NVIDIA NeMo FastConformer via
  sherpa-onnx) that emits captions word by word, roughly half a second behind the
  speaker, instead of re-transcribing each phrase. Much lighter on the CPU than
  Whisper. Its text is lowercase and unpunctuated. Downloads a 456 MB model on first
  use.
- **Whisper** — the default, with a model picker from Tiny to Large v3.
- **Windows speech (SAPI)** — instant, no download, microphone only.

### Captions exclude your screen reader

**Audio ▸ Exclude screen reader speech** (on by default) removes NVDA, JAWS, or
Narrator from whole-system capture, so a transcript of a meeting is not interleaved
with your own screen reader's narration — including its announcements of the captions
themselves.

Some audio devices cannot support per-process capture at all. The app detects that
within a few seconds, explains it, and falls back to capturing everything rather than
silently captioning nothing.

### Under the hood

- Now built on **.NET 10 LTS** (supported to November 2028), which brings ARM64
  garbage-collection improvements that reduce caption jitter.
- Refreshed engines: Whisper.net 1.9.1, NAudio 2.3.0, System.Speech 10.0.10,
  sherpa-onnx 1.13.4.
- Both downloads are signed with Azure Artifact Signing, so Windows shows a verified
  publisher rather than "Unknown publisher".

### Accessibility

- The transcript is the only focusable control; review it line by line with the arrow
  keys. `Tab` does nothing, and new captions never steal focus while you read.
- Finalized lines are announced once each via UI Automation notifications; `F8`
  toggles that off.
- Caption text scales from 14 to 48 point with `Ctrl +` / `Ctrl −`, and the app uses
  system colors so High Contrast themes apply automatically.

### Installing without the mouse

```powershell
Add-AppxPackage -Path .\AccessibleLiveCaptions-v0.3.0-win-arm64.msix
```

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
