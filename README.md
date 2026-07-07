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
video — via WASAPI loopback), using either an **accurate on-device Whisper** engine or
Windows' instant built-in speech, and presents the result in a fully accessible,
reviewable transcript.

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

## Requirements

- Windows 10/11 (built and verified on a Snapdragon X Elite / ARM64 Copilot+ PC)
- .NET 8 SDK
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
| **System audio** | Anything the PC is playing — Teams, YouTube, a podcast | **WASAPI loopback** on the default render device |

Both paths are normalized to 16 kHz mono and fed to the same engine, so the recognizer
never knows or cares where the audio came from.

## Choosing an engine

| Engine | Accuracy | Latency | Notes |
| --- | --- | --- | --- |
| **Whisper (on-device)** | High | ~1 s (segment-based) | Fully local, works with mic **and** system audio; native on ARM64. First run downloads the model. |
| **Windows speech (SAPI)** | Modest | Instant | Ships with Windows, zero download, but **microphone only**. |

## Keyboard shortcuts

| Action | Shortcut |
| --- | --- |
| Start / stop listening | `Ctrl+R` |
| Go to transcript | `Ctrl+T` |
| Review lines | `↑` `↓` `Home` `End` (inside the transcript) |
| Larger / smaller text | `Ctrl +` / `Ctrl −` |
| Toggle screen-reader announcements | `F8` |
| Show / hide timestamps | View menu |
| Copy all | `Ctrl+Shift+C` |
| Save transcript | `Ctrl+S` |
| Clear | `Ctrl+L` |

Everything is also on the **Captions** and **View** menus (with `Alt` access keys) and
the toolbar.

## Trying it with a screen reader

1. Turn on **Narrator** (`Ctrl+Win+Enter`) or NVDA.
2. Press **Start listening** and speak — each finished sentence is announced.
3. Press `F8` to turn announcements off, then `Ctrl+T` and use the arrow keys to
   review the transcript at your own pace. This is the review experience the built-in
   feature lacks.

## How it's built

Two clean seams keep the design honest — **where the audio comes from** and **what
turns it into text** are fully decoupled from **how captions are presented**.

```
Audio/IAudioCaptureSource   – interface: emits normalized 16 kHz mono float frames
  ├─ MicrophoneCaptureSource      – WASAPI capture of the default mic
  └─ SystemAudioCaptureSource     – WASAPI render loopback (everything you hear)
        (downmix to mono + resample to 16 kHz handled in WasapiCaptureSource)

Speech/ICaptionSource       – interface: Start/Stop + PartialRecognized / FinalRecognized / StateChanged
  ├─ WhisperCaptionSource         – on-device Whisper.net; consumes an IAudioCaptureSource,
  │                                 does energy-VAD segmentation, emits interim + final
  └─ SystemSpeechCaptionSource    – offline SAPI dictation (mic only)

Accessibility/ScreenReader  – speaks text to a screen reader via the UIA Notification event
MainWindow                  – the accessible UI (scrollable transcript, live region, shortcuts)
TranscriptLine              – one finalized caption = one focusable list item
```

The UI depends only on `ICaptionSource`; the engine depends only on
`IAudioCaptureSource`. You can swap either seam without touching the other.

## Making it even better

- **Larger Whisper model** — change `GgmlType.BaseEn` to `SmallEn` in
  `WhisperCaptionSource` for higher accuracy (bigger download, still real-time on X Elite).
- **NPU acceleration** — Whisper.net here uses the CPU runtime. The Snapdragon's Hexagon
  NPU can be targeted via **ONNX Runtime + the QNN execution provider** running a Whisper
  ONNX model — the same class of on-device acceleration Copilot+ Live Captions uses. That's
  a larger build (mel-spectrogram + tokenizer + encoder/decoder), but it drops into the same
  `ICaptionSource` seam.
- **Punctuation / diarization / language switching** — all live inside the engine class;
  the accessible UI is unaffected.

## The point for Microsoft

The built-in Live Captions already has a good on-device recognizer — what it lacks is an
**accessible surface**: a full, scrollable, screen-reader-navigable transcript with
adjustable text, announcements you control, and system-audio capture. This demo shows all
of that is straightforward, on-device, on a Copilot+ PC, today.
