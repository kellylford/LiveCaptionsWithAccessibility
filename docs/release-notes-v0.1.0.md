# Accessible Live Captions v0.1.0 — Release Notes

The first public build of **Accessible Live Captions**, a small native Windows app
that demonstrates how live captioning *should* be presented for screen-reader and
low-vision users. It transcribes your microphone or any audio playing on the PC and
puts every line into a full, scrollable, keyboard-navigable transcript that screen
readers read naturally — the review experience Windows' built-in Live Captions lacks.

Everything runs **on-device**: no cloud, no API keys, no account. On a Snapdragon X
(ARM64) Copilot+ PC the Whisper CPU runtime is native and comfortably real-time.

## Download

| Download | When to use |
|----------|-------------|
| **`AccessibleLiveCaptions-v0.1.0-win-arm64.zip`** | Windows on ARM64 (Snapdragon X / Copilot+ PC). Self-contained — unzip and run `AccessibleLiveCaptions.exe`; **no .NET install required**. |

> This is an ARM64 build, matching the Copilot+ PC hardware the demo is about. To run
> on another architecture, build from source with `dotnet run` (the project retargets
> with a one-line `RuntimeIdentifier` change).

The first time you use the **Whisper** engine, the app downloads the on-device model
(`ggml-base.en`, ~141 MB) to `%LOCALAPPDATA%\AccessibleLiveCaptions\models` and reuses
it thereafter. Everything after that is offline.

## What it does

- **Captions your microphone or any system audio.** Microphone (WASAPI capture),
  everything you hear (WASAPI render loopback), or **one specific application** — e.g.
  only the browser or only Teams — via the Windows 11 process-loopback API.
- **Two engines.** **Whisper** (accurate, on-device, works with mic *and* system audio;
  native on ARM64) or **Windows speech / SAPI** (instant, ships with Windows, mic only).
- **Selectable Whisper model** from Tiny (fastest) through Large v3, trading speed for
  accuracy; models download on demand and can be bulk-downloaded or cleared from the menu.

## Why it's more accessible

| Problem with built-in Live Captions | What this demo does |
| --- | --- |
| Only a few words visible; history is gone | **Scrollable transcript** holding the entire session |
| Can't review with a screen reader | Every finalized line is its **own focusable list item** — arrow keys, Home/End to review line by line |
| Silent to screen readers | New captions are spoken via the **UIA Notification event** (Narrator/NVDA/JAWS), and it's **toggleable** (F8) so you control verbosity |
| Tiny fixed text | **Adjustable caption size** (Ctrl +/−, 14–48 pt) |
| Ignores your theme | Uses **system colors**, so Windows **High Contrast** themes just work |
| No way to keep it | **Copy all** (Ctrl+Shift+C) / **Save to .txt** (Ctrl+S) |

Interim ("still deciding") words appear under **Now hearing** but are *not* announced on
every change — that would flood a screen reader. Only settled lines are announced.

## Keyboard model

The app behaves like a document with a menu bar. Focus stays on the transcript — the
only content control — and `Tab` does nothing. You review captions with the arrow keys
and reach every command from the menu bar (`Alt` or `F10`).

| Action | Key |
| --- | --- |
| Review lines | `↑` `↓` `Home` `End` |
| Open the menu bar | `Alt` or `F10` |
| Start / stop listening | `Ctrl+R` |
| Larger / smaller text | `Ctrl +` / `Ctrl −` |
| Toggle screen-reader announcements | `F8` |
| Copy all | `Ctrl+Shift+C` |
| Save transcript | `Ctrl+S` |
| Clear | `Ctrl+L` |

## Requirements

- Windows 10/11 (built and verified on a Snapdragon X Elite / ARM64 Copilot+ PC)
- For the Whisper engine's first run: an internet connection to fetch the model (once)
- For the Windows-speech engine: an installed recognizer language pack (e.g. en-US)
- A microphone for mic mode; system-audio mode needs no microphone

## Known limitations

- **ARM64 build only** in this release (see Download). Other architectures build from source.
- **Whisper runs on the CPU.** The Snapdragon Hexagon NPU is not yet used; NPU
  acceleration (ONNX Runtime + QNN execution provider) is a documented future step.
- **Windows speech (SAPI) captions the microphone only** — system audio requires Whisper.
- Larger Whisper models (Medium and above) may lag live speech; Base is the default balance.

## The point

Windows' built-in Live Captions already has a good on-device recognizer — what it lacks
is an **accessible surface**: a full, scrollable, screen-reader-navigable transcript with
adjustable text, announcements you control, and system-audio capture. This 0.1 shows all
of that is straightforward, on-device, on a Copilot+ PC, today.
