# Accessible Live Captions (demonstration)

A small native Windows app that shows **the right way to present live captions for
screen-reader and low-vision users** — the thing Windows' built-in Live Captions
gets wrong.

Windows Live Captions shows only a sliver of text, doesn't expose a reviewable
history, and is essentially invisible to Narrator / NVDA / JAWS. This demo fixes the
*presentation*: it transcribes your **microphone** in real time and puts every line
into a **full, scrollable, keyboard-navigable transcript** that screen readers read
naturally.

It can caption **either your microphone or any audio playing on the PC** (a meeting, a
video, one specific app), with a choice of four on-device engines — accurate Whisper,
a true word-by-word **streaming** recognizer, the **Windows on-device (NPU)** recognizer
on Copilot+ PCs, or Windows' instant built-in speech — and presents the result in a
fully accessible, reviewable transcript. Because its audience runs screen readers, the
whole-system capture can **exclude your screen reader's own speech** (on by default),
so captions cover the meeting, not your reader's narration.

> Everything runs **on-device** — no cloud, no API keys. On a Snapdragon X (ARM64)
> Copilot+ PC the Whisper CPU runtime is native and comfortably real-time. That's the
> point for Microsoft: accurate, accessible, live captioning of any system audio is
> clearly achievable on this hardware today.

## Why this is more accessible

| Problem with built-in Live Captions | What this demo does |
| --- | --- |
| Only a few words visible; history is gone | **Scrollable transcript ListBox** holding the entire session |
| Can't review with a screen reader | Every finalized line is its **own focusable list item** — arrow up/down, Home/End to review line by line |
| Silent to screen readers | New captions are spoken via the **UIA Notification event** (Narrator/NVDA/JAWS), and it's **toggleable** so you control verbosity |
| Tiny fixed text | **Adjustable caption size** (Ctrl +/−, 14–48pt) |
| Ignores your theme | Uses **system colors**, so Windows **High Contrast** themes just work |
| No way to keep it | **Copy all** / **Save to .txt** |

Interim ("still deciding") words appear under **Now hearing** but are *not*
announced on every change — that would flood a screen reader. Only settled lines are
announced.

## Install

From the [Releases page](https://github.com/kellylford/LiveCaptionsWithAccessibility/releases),
both downloads code-signed:

| Download | What you get |
| --- | --- |
| **`.msix`** | The full app **including the Windows on-device (NPU) engine**. Double-click to install — no Developer Mode, no certificate steps. Keyboard alternative: `Add-AppxPackage -Path .\<file>.msix` |
| **`-portable.zip`** | Unzip and run; no installation. No NPU engine — Windows grants that only to installed apps. |

Currently **ARM64 (Snapdragon) Windows 11 only** — this is a demonstration build. See
[the user guide](docs/UserGuide.md) for everything else.

## Requirements

- Windows 10/11 (built and verified on a Snapdragon X Elite / ARM64 Copilot+ PC)
- .NET 10 SDK (only to build from source; the downloads are self-contained)
- A microphone (for mic mode) — system-audio mode needs no mic
- For the Windows-speech engine: an installed recognizer language pack (en-US)

## Run it

```powershell
dotnet run
```

Pick your **Audio source** and **Engine**, then press **Start listening**.

- **First time with the Whisper engine**, it downloads the on-device model
  (`ggml-base.en`, ~142 MB) to `%LOCALAPPDATA%\AccessibleLiveCaptions\models` and
  reuses it thereafter. Everything after that is offline.
- The first time it uses the **microphone**, Windows may prompt for mic permission.
- **System audio** mode captures render loopback, so just play something and captions
  appear — no microphone or "Stereo Mix" needed.

## Capturing microphone vs. system audio

| Audio source | What it captions | How |
| --- | --- | --- |
| **Microphone** | You / people in the room | WASAPI capture of the default mic |
| **System audio → (All)** | Everything the PC is playing | **WASAPI loopback** on the default render device |
| **System audio → an app** | Just one application — e.g. only the browser or only Teams | **Process loopback** (`ActivateAudioInterfaceAsync` + `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS`) |

When **System audio** is selected, the **Application** picker lets you limit capture to a
single running app (and its child processes) instead of the whole mix — useful for
captioning one meeting without your music bleeding in. It lists apps that have a window;
open the dropdown to refresh. Per-app capture uses the Windows 11 process-loopback API.

**Exclude screen reader speech** (Audio menu, on by default) uses process loopback's
*exclude* mode for the whole-mix case: the running screen reader's process tree
(NVDA / JAWS / Narrator) is removed from the captured audio, so a screen-reader user can
caption "everything I hear" without their own reader's voice — or its announcements of
the captions themselves — polluting the transcript.

All capture paths are normalized to 16 kHz mono and fed to the same engine, so the
recognizer never knows or cares where the audio came from.

## Choosing an engine

| Engine | Words appear | Text style | Notes |
| --- | --- | --- | --- |
| **Whisper (on-device)** | Per phrase, ~1 s behind | Punctuated | Fully local, mic **and** system audio; model picker from Tiny to Large v3. First run downloads the model. |
| **Streaming (on-device)** | **Word by word, ~½ s behind** | Lowercase | NeMo streaming FastConformer transducer via sherpa-onnx (native win-arm64). Lowest latency, very light CPU (RTF ≈ 0.10 on X Elite). 456 MB one-time download. |
| **Windows on-device (NPU)** | Per phrase, fast | Punctuated | The OS Live Captions recognizer via `Microsoft.Windows.AI.Speech` — runs on the Hexagon NPU on Copilot+ PCs at near-zero CPU cost, model preinstalled. Requires the MSIX-packaged flavor (`packaging/`), Windows 11 24H2+; API currently experimental, English-only. |
| **Windows speech (SAPI)** | Word by word, instant | Plain | Ships with Windows, zero download, but **microphone only**. |

## Keyboard model

The app works like a document with a menu bar. **Focus stays on the transcript** — the
only content control — and `Tab` does nothing. You review captions with the arrow keys
and reach every command from the menu bar (`Alt` or `F10`), so there is no tabbing
through toolbars.

| Action | Key |
| --- | --- |
| Review lines | `↑` `↓` `Home` `End` |
| Open the menu bar | `Alt` or `F10`, then arrow keys |
| Start / stop listening | `Ctrl+R` |
| Larger / smaller text | `Ctrl +` / `Ctrl −` |
| Toggle screen-reader announcements | `F8` |
| Copy all | `Ctrl+Shift+C` |
| Save transcript | `Ctrl+S` |
| Clear | `Ctrl+L` |

All commands live on the **Captions**, **Audio**, and **View** menus (each with `Alt`
access keys). Audio source, per-app capture, and engine are radio choices under
**Audio**.

## Trying it with a screen reader

1. Turn on **Narrator** (`Ctrl+Win+Enter`) or NVDA.
2. Press **Start listening** and speak — each finished sentence is announced.
3. Press `F8` to turn announcements off, then use the arrow keys to review the
   transcript at your own pace — focus already lives on the transcript. This is the
   review experience the built-in feature lacks.

## How it's built

Two clean seams keep the design honest — **where the audio comes from** and **what
turns it into text** are fully decoupled from **how captions are presented**.

```
Audio/IAudioCaptureSource   – interface: emits normalized 16 kHz mono float frames
  ├─ MicrophoneCaptureSource               – WASAPI capture of the default mic
  ├─ SystemAudioCaptureSource              – WASAPI render loopback (everything you hear)
  ├─ ProcessAudioCaptureSource             – process loopback: one app + its children
  └─ SystemAudioExceptProcessCaptureSource – process loopback EXCLUDE mode: everything
        except one process tree (how the screen reader's voice is removed)
        (downmix to mono + resample to 16 kHz handled in WasapiCaptureSource)

Speech/ICaptionSource       – interface: Start/Stop + PartialRecognized / FinalRecognized / StateChanged
  ├─ WhisperCaptionSource         – on-device Whisper.net; consumes an IAudioCaptureSource,
  │                                 does energy-VAD segmentation, emits interim + final
  ├─ SherpaCaptionSource          – true-streaming NeMo transducer (word-by-word partials)
  ├─ WindowsAiCaptionSource       – Windows AI flavor: the OS recognizer (NPU on Copilot+)
  └─ SystemSpeechCaptionSource    – offline SAPI dictation (mic only)

Accessibility/ScreenReader  – speaks text to a screen reader via the UIA Notification event
MainWindow                  – the accessible UI (scrollable transcript, live region, shortcuts)
TranscriptLine              – one finalized caption = one focusable list item
```

The UI depends only on `ICaptionSource`; the engine depends only on
`IAudioCaptureSource`. You can swap either seam without touching the other.

## NPU acceleration

Done — via the front door. On Copilot+ PCs the **Windows on-device engine** runs the OS
speech model on the Hexagon NPU through `Microsoft.Windows.AI.Speech` (the sanctioned API
over the same engine Live Captions uses). It needs MSIX packaging with the
`systemAIModels` capability and the experimental Windows App SDK channel, so it ships as
an opt-in flavor: run `packaging\build-windows-ai.ps1` (Developer Mode required) to build
and register it locally. Whisper-on-NPU directly was evaluated and rejected: whisper.cpp
has no Hexagon backend on Windows, and hand-building Whisper on ONNX Runtime + QNN buys
~no speed over the 12-core Oryon CPU — the NPU's real win (power) comes free with the
Windows API.

## Making it even better

- **Punctuation for the Streaming engine** — a sherpa-onnx online punctuation model would
  fix its lowercase output; the accessible UI is unaffected.
- **Diarization / language switching** — all live inside the engine class.

## Documentation & building

- **[docs/ProjectStatus.md](docs/ProjectStatus.md)** — **start here if you are picking this
  project up**, or setting it up on a new machine: current state, build and release
  pipeline, signing setup, fresh-install troubleshooting, and the hardware-specific traps.
- **[docs/UserGuide.md](docs/UserGuide.md)** — the user guide: keyboard reference, choosing
  sources and engines, screen-reader exclusion, models, troubleshooting.
- **[docs/LiveCaptionSpec.md](docs/LiveCaptionSpec.md)** — a formal, build-it-again specification: the design
  principles, the two decoupling seams, the accessibility/keyboard contract, and a
  file-by-file map.
- **[docs/release-notes-v0.1.0.md](docs/release-notes-v0.1.0.md)** — the 0.1 release notes.
- **Build locally:** `dotnet build` or double-click **`build.bat`**.
- **CI:** `.github/workflows/build.yml` builds on demand (Actions ▸ *Build* ▸ **Run
  workflow**). `.github/workflows/release.yml` publishes a self-contained win-arm64 zip as a
  GitHub Release whenever a `v*` tag (e.g. `v0.1.0`) is pushed, reading its notes from
  `docs/release-notes-<tag>.md`.

## The point for Microsoft

The built-in Live Captions already has a good on-device recognizer — what it lacks is an
**accessible surface**: a full, scrollable, screen-reader-navigable transcript with
adjustable text, announcements you control, and system-audio capture. This demo shows all
of that is straightforward, on-device, on a Copilot+ PC, today.
