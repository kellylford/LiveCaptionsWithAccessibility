## Accessible Live Captions 0.2.0

**Which download do I want?**

- **`.msix`** — the full app, including the Windows on-device (NPU) engine. Double-click
  to install; it is code-signed, so no Developer Mode and no certificate steps.
- **`-portable.zip`** — no installation, no NPU engine. Unzip and run.

Both require a **Windows on ARM (Snapdragon) PC running Windows 11**. This release is a
demonstration build and is ARM64-only — it will not run on an Intel or AMD PC.

### New: three ways to recognize speech

Choose under **Audio ▸ Engine**.

- **Windows on-device (NPU)** — the same on-device recognizer Windows Live Captions uses,
  reached through the Windows AI Speech API. On a Copilot+ PC it runs on the neural
  processing unit, so captions cost almost no CPU or battery, and the text arrives with
  full punctuation and capitalization. English only. MSIX download only.
- **Streaming** — a true streaming recognizer (NVIDIA NeMo FastConformer via sherpa-onnx)
  that emits captions word by word, roughly half a second behind the speaker, instead of
  re-transcribing each phrase. Much lighter on the CPU than Whisper. Its text is lowercase
  and unpunctuated. Downloads a 456 MB model on first use.
- **Whisper** — unchanged, still the default, with the same model picker from Tiny to
  Large v3.

### New: captions no longer include your screen reader

**Audio ▸ Exclude screen reader speech** (on by default) removes NVDA, JAWS, or Narrator
from whole-system capture, so a transcript of a meeting is not interleaved with your own
screen reader's narration — including its announcements of the captions themselves.

Some audio devices cannot support per-process capture at all. The app now detects that
within a few seconds, explains it, and falls back to capturing everything rather than
silently captioning nothing.

### Fixes

- The engine menu no longer disables the Windows on-device entry because of a transient
  check at startup — a case where screen readers announced four items but the arrow keys
  reached only three.
- Captions no longer invent phrases such as "soft music" or "keyboard clicking" when a
  capture path goes silent; the cause is detected and reported instead.

### Documentation

A full [user guide](https://github.com/kellylford/LiveCaptionsWithAccessibility/blob/main/docs/UserGuide.md)
covers the keyboard model, choosing sources and engines, screen-reader exclusion, model
management, and troubleshooting.

### Installing without the mouse

The `.msix` can be installed entirely from the keyboard:

```powershell
Add-AppxPackage -Path .\AccessibleLiveCaptions-v0.2.0-win-arm64.msix
```

### Known limitations

- ARM64 only; there is no Intel/AMD build in this release.
- The NPU engine uses a preview Windows API. It is English-only, and a future Windows
  update could change its behavior. The other engines are unaffected.
- Per-application capture and screen-reader exclusion do not work on audio devices that
  do not support per-process capture; the app tells you when it hits one.
