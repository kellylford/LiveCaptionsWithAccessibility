# Accessible Live Captions — Product & Engineering Spec

> A build-it-again specification. Someone handed only this document should be able to
> reconstruct the app: what it is, why it is shaped the way it is, every user-facing
> behavior, the architecture and its two decoupling seams, and the accessibility and
> keyboard contract that are the whole point.
>
> Structure follows the QuickMail AI-collaborative spec template, adapted from a single
> feature to a whole (small) application.

---

## 1. Executive Summary

Windows ships **Live Captions**, but its presentation is hostile to screen-reader and
low-vision users: it shows only a sliver of text, keeps no reviewable history, is
effectively invisible to Narrator/NVDA/JAWS, uses tiny fixed text, and can't capture
one specific app. **Accessible Live Captions** is a small native Windows (WPF/.NET 8)
app that fixes the *presentation*. It transcribes the microphone or any audio playing on
the PC, on-device, and puts every finalized line into a full, scrollable,
keyboard-navigable transcript where each line is its own focusable, screen-reader-readable
item. It exists to demonstrate to Microsoft that accurate, accessible, live captioning of
any system audio is straightforward on a Copilot+ PC today.

**Who benefits:** blind and low-vision users who need to review captions (not just glimpse
them), and accessibility engineers who want a reference for the correct pattern.

---

## 2. User Problem & Opportunity

### 2.1 Current state

| Surface | Windows Live Captions today | Pain | Who feels it |
|---|---|---|---|
| Caption history | A few words, then gone | Can't re-read what was missed | Everyone; acutely, screen-reader users |
| Screen-reader access | Not exposed as reviewable text | Can't navigate captions at all | Blind users |
| Announcements | None you control | Either nothing, or (elsewhere) a flood | Screen-reader users |
| Text size | Small, fixed | Unreadable for low vision | Low-vision users |
| Theming | Own chrome | Ignores High Contrast | High-Contrast users |
| Source scope | System-wide | Can't isolate one app | Anyone in a call with music playing |

### 2.2 Target personas

1. **Blind screen-reader user** — wants to *review* a meeting's captions line by line at
   their own pace, and to hear new lines announced without being flooded.
2. **Low-vision user** — wants large, high-contrast, resizable caption text that respects
   Windows High Contrast.
3. **Accessibility engineer / Microsoft team** — wants a concrete, minimal reference
   implementation of the accessible-captions pattern on Copilot+ hardware.

### 2.3 Why now

Copilot+ PCs (Snapdragon X, ARM64) make accurate on-device speech-to-text cheap and
native (Whisper.net's CPU runtime runs natively on win-arm64 — verified: ~3 s of audio
transcribed in ~640 ms with the tiny model). The recognizer is a solved problem; the
accessible *surface* is what's missing, and that's a UI problem this app solves.

---

## 3. Design Principles

1. **Presentation is the product.** The recognizer is swappable and deliberately not the
   focus; the accessible transcript is.
2. **On-device only.** No cloud, no API keys, no account. Privacy and offline operation
   are non-negotiable.
3. **The transcript is a document.** Focus lives on it; you review with arrow keys. `Tab`
   does nothing. Commands come from a menu bar reached with `Alt`/`F10`.
4. **Never flood the screen reader.** Only finalized lines are announced, and even that is
   user-toggleable. Interim guesses are shown, never spoken on every change.
5. **Defer to the system.** System colors (so High Contrast just works), per-monitor DPI
   awareness, standard Windows shortcuts.
6. **Two clean seams.** *Where audio comes from* and *what turns it into text* are fully
   decoupled from *how captions are presented*. Either can be swapped without touching the
   other.

---

## 4. Feature Scope & Acceptance Criteria

### 4.1 In scope (v0.1)

| Feature | Control / Shortcut | Default | Notes |
|---|---|---|---|
| Start / stop listening | Captions menu / `Ctrl+R` | Stopped | Rebuilds the caption source for the current selections |
| Audio source: Microphone | Audio ▸ Source | — | WASAPI capture of the default mic |
| Audio source: System audio | Audio ▸ Source | **Selected** | WASAPI render loopback (everything you hear) |
| Per-app capture | Audio ▸ Application | (All system audio) | Process loopback; menu rebuilds on open to list current apps |
| Exclude screen reader speech | Audio menu | **On** | Whole-mix capture excludes the running screen reader's process tree (NVDA/JAWS/Narrator) |
| Engine: Whisper | Audio ▸ Engine | **Selected** | On-device; mic **and** system audio |
| Engine: Streaming | Audio ▸ Engine | — | True word-by-word streaming (NeMo FastConformer via sherpa-onnx); mic **and** system audio |
| Engine: Windows on-device (NPU) | Audio ▸ Engine | — | Windows AI flavor only (`-p:WindowsAI=true`, MSIX); the OS Live Captions recognizer, NPU-accelerated on Copilot+ |
| Engine: Windows speech (SAPI) | Audio ▸ Engine | — | Instant; **microphone only** |
| Whisper model picker | Audio ▸ Whisper model | Base (en) | Tiny → Large v3; downloads on demand |
| Download all models | Audio menu | — | Fetches every model (~7 GB) with progress |
| Clear downloaded models | Audio menu | — | Deletes model files; skips any in use |
| Larger / smaller text | View menu / `Ctrl +` `Ctrl −` | 22 pt | Clamped 14–48 pt |
| Show timestamps | View menu | Off | Per-line `[HH:mm:ss]` prefix |
| Announce new captions | View menu / `F8` | **On** | Speaks finalized lines to the screen reader |
| Copy all | Captions menu / `Ctrl+Shift+C` | — | Whole transcript to clipboard |
| Save transcript | Captions menu / `Ctrl+S` | — | `.txt`, honors the timestamp setting |
| Clear transcript | Captions menu / `Ctrl+L` | — | — |

### 4.2 Explicitly out of scope (v0.1)

- No persistence of transcripts across runs (Save is manual, to a file).
- No punctuation/diarization/language-switching UI (all live inside the engine class).
- The Streaming engine emits lowercase, unpunctuated text (a transducer property); a
  punctuation model is a possible future refinement.
- No installer/auto-update (portable zip only; QuickMail's Velopack model is not adopted).
- No architectures other than win-arm64 in shipped binaries (build from source for x64).
- SAPI cannot caption system audio; that pairing is intentionally disallowed in the UI.

---

## 5. Architecture & Technical Decisions

### 5.1 The two seams

```
Audio/IAudioCaptureSource      — emits normalized 16 kHz mono float frames + Failed
  ├─ MicrophoneCaptureSource       — WASAPI capture of the default mic
  ├─ SystemAudioCaptureSource      — WASAPI render loopback (everything you hear)
  ├─ ProcessAudioCaptureSource     — Windows process loopback (one app + its children)
  └─ SystemAudioExceptProcessCaptureSource — process loopback EXCLUDE mode: the whole
        mix minus one process tree (used to remove the screen reader's own speech)
        (all four subclass WasapiCaptureSource: downmix to mono + resample to 16 kHz;
         the process-loopback pair wraps ProcessLoopbackWaveIn, a hand-rolled IWaveIn)

Accessibility/ScreenReaderDetector — finds the running screen reader (NVDA/JAWS/Narrator)

Speech/ICaptionSource          — Start/Stop + PartialRecognized / FinalRecognized / StateChanged
  ├─ WhisperCaptionSource          — on-device Whisper.net; consumes an IAudioCaptureSource,
  │                                  energy-VAD segmentation, emits interim + final
  ├─ SherpaCaptionSource           — true-streaming NeMo FastConformer transducer (sherpa-onnx);
  │                                  word-by-word partials, endpoint rules finalize lines
  ├─ WindowsAiCaptionSource        — Windows AI flavor only: the OS on-device recognizer
  │                                  (Microsoft.Windows.AI.Speech; NPU on Copilot+ PCs)
  └─ SystemSpeechCaptionSource     — offline SAPI dictation (mic only; owns its own audio)

Accessibility/ScreenReader     — speaks text via the UIA Notification event (no focus change)
MainWindow                     — the accessible UI (menu bar, live region, transcript ListBox)
TranscriptLine                 — one finalized caption = one focusable, INotifyPropertyChanged item
WhisperModelStore              — model file location, presence, download-with-progress, clear
```

**The UI depends only on `ICaptionSource`. The Whisper engine depends only on
`IAudioCaptureSource`.** SAPI is the exception that proves the rule: it owns its own audio
device (`SetInputToDefaultAudioDevice`), so it does not consume an `IAudioCaptureSource` —
which is exactly why it is microphone-only.

### 5.2 Key decisions

**Decision: Whisper.net CPU runtime as the primary engine.**
Alternatives: Azure Speech SDK (cloud, not first-class on win-arm64 — ruled out, violates
on-device principle); Windows on-device recognizer (not cleanly exposed for arbitrary audio).
Rationale: Whisper.net's CPU runtime is verified native on win-arm64 and high-accuracy;
SAPI stays as a zero-download instant fallback.

**Decision: A true streaming engine (sherpa-onnx) alongside Whisper.**
Whisper is chunked: the utterance-so-far is re-transcribed every ~700 ms, so caption
latency is the utterance length plus decode time and CPU cost grows with utterance
length. A streaming transducer decodes incrementally — words appear ~0.5 s after they
are spoken at a fraction of the compute (measured RTF ≈ 0.10 on a Snapdragon X Elite).
Model: NVIDIA NeMo cache-aware streaming FastConformer transducer, 480 ms-latency
export for sherpa-onnx (the 80 ms export was measurably less accurate — dropped word
endings — while 480 ms reproduced the reference transcript exactly). sherpa-onnx ships
first-party win-arm64 NuGet runtimes. Two engine-specific mitigations live in
`SherpaCaptionSource`: system loopback delivers no frames while nothing renders, so the
worker feeds wall-clock-paced silence to keep endpoint rules honest; and on Stop the
stream is padded with 0.8 s of silence so the encoder has right-context to decode the
final word. Transducer output is lowercase/unpunctuated; finals get a leading capital.

**Decision: The Windows on-device recognizer as an opt-in flavor, not the default.**
`Microsoft.Windows.AI.Speech` (Windows App SDK 2.2-experimental) exposes the OS Live
Captions model family with streaming Recognizing/Recognized events and a
`SpeechAudioProvider.PushData` input that accepts our 16 kHz mono capture — NPU-backed
and effectively free on Copilot+ PCs. But it requires MSIX packaging with the
`systemAIModels` capability and an experimental SDK, so it is gated behind
`-p:WindowsAI=true` (see `packaging/`): the portable zip stays dependency-light, and the
packaged flavor adds the "Windows on-device" engine. When the engine cannot run
(unpackaged, pre-24H2), its menu item stays visible but disabled with the reason in the
tooltip/HelpText. Whisper-on-NPU directly was evaluated and rejected: whisper.cpp has no
Hexagon backend on Windows, and ONNX-Runtime-QNN Whisper needs a hand-built decode
pipeline for ~no speed gain over the 12-core Oryon CPU (the NPU win is power, which the
Windows API already delivers).

**Decision: Real-time segmentation over a non-streaming recognizer.**
Whisper is not a streaming recognizer. A single worker thread accumulates audio into an
"utterance", a simple energy VAD (RMS threshold) tracks trailing silence, every ~700 ms the
utterance-so-far is re-transcribed and emitted as an **interim** caption, and when ~0.6 s of
silence follows real speech (or the utterance exceeds ~12 s) it is transcribed once more and
emitted as a **final** line, then reset. One worker thread owns the processor so inference
never overlaps.

**Decision: Exclude the screen reader from whole-mix capture, on by default.**
The primary audience runs a screen reader, so "caption what I hear" naïvely includes the
reader's own narration — interleaving it with the meeting/video captions and re-captioning
the app's own announcements (a feedback loop). Process loopback's EXCLUDE mode
(`PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE`) removes one process tree from the
mix; `ScreenReaderDetector` finds the running reader (NVDA/JAWS/Narrator, first match
wins — the API takes a single tree) and whole-mix capture targets it. Verified with JAWS
live: with exclusion on, test sentences transcribed exactly and the capture was digitally
silent while JAWS spoke. Audio ▸ "Exclude screen reader speech" (default on) disables when
a specific app is being captured — a single app's audio never contains the reader anyway.

**Caveat and mitigation: on some audio devices the process-loopback tap is dead.**
Observed with a "Hi-Res Audio" headphone output (likely hardware-offloaded rendering):
process loopback delivers only silent packets in BOTH include and exclude modes, while
plain device loopback hears everything — so exclusion or per-app capture would silently
caption nothing (and a recognizer fed silence hallucinates, e.g. "soft music").
`ProcessLoopbackWatchdog` compares captured energy against the peak meters of the audio
sessions that should be reaching the capture (screen-reader speech processes are filtered
from the reference so the reader speaking alone can't false-trigger). After ~3 s of
"reference audible, capture silent": exclude mode announces the problem, turns the toggle
off, and restarts on plain loopback (captions win over exclusion); per-app mode announces
that this device can't do per-app capture and suggests (All system audio). Verified live:
Netflix on the offloaded device produced no captions under exclusion, and the fallback
restarted and captioned it within seconds.

**Decision: Process loopback via raw COM interop.**
Per-app capture needs `ActivateAudioInterfaceAsync` with `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS`
(Windows 10 20H1+/11), which NAudio does not wrap. `ProcessLoopbackWaveIn` implements NAudio's
`IWaveIn` over hand-declared COM interfaces so it drops into the existing resample pipeline.
It runs on a dedicated **MTA** thread: the activation callback arrives on an MTA thread, and
creating the completion handler there avoids COM marshalling back to the STA UI thread, which
would deadlock while awaiting activation.

**Decision: UIA Notification event for announcements.**
Alternatives: move focus (disruptive); a persistent live region (coarse control). Rationale:
`RaiseNotificationEvent` (UIA 3, supported by WPF on .NET Core 3+) announces without moving
focus, and its processing hint chooses queue-in-order (`All`, for finalized lines) vs.
supersede (`MostRecent`, for transient status).

**Decision: System colors + PerMonitorV2 DPI.**
All brushes are `DynamicResource {x:Static SystemColors.*}` so Windows High Contrast is
automatic; the manifest declares PerMonitorV2 so scaled displays stay crisp.

### 5.3 Audio normalization contract

Every capture source hands consumers the **same** format: 16 kHz, mono, `float` in [-1, 1].
`WasapiCaptureSource.DecodeToMono` handles arbitrary device formats (IEEE float, 16- or
32-bit PCM), averages channels to mono, and a `WdlResampler` in feed mode resamples to 16 kHz.
The recognizer never knows or cares where the audio originated.

### 5.4 Shared-component note

The app is small enough that there is no hidden second consumer of any class. The one
cross-cutting piece is `WhisperModelStore`, used by both `WhisperCaptionSource`
(fetch-on-first-use) and `MainWindow`'s "Download all"/"Clear" commands — both go through the
same `EnsureAsync`/`ClearAll`, so there is a single source of truth for model files and their
naming (`ggml-<GgmlType>.bin` locally; the Hugging Face filename mapping lives only in
`ModelFileName`).

---

## 6. Keyboard Walkthrough

### Path: Start captioning system audio (default)

1. App opens. **Expected:** Focus is on the (empty) transcript; status reads "Ready. Press
   Start listening (Control R) to begin." Default selections are System audio + Whisper.
2. User presses `Ctrl+R`. **Expected:** Status announces progress (model prepare/load →
   "Listening…"), announced as interrupting status. On first Whisper use, download-progress
   status lines appear.
3. Audio plays. **Expected:** Interim text updates under "Now hearing" (shown, not spoken).
   Each finalized sentence is appended as a new transcript line and, if announcements are on,
   spoken in order.
4. User presses `↑`/`↓`/`Home`/`End`. **Expected:** Focus moves item to item; each line reads
   as its caption text. New incoming lines do **not** steal focus while reviewing.
5. User presses `Ctrl+R`. **Expected:** Listening stops; status "Stopped."; menu item returns
   to "Start listening".

### Path: Capture one application

1. `Alt` → Audio ▸ Application. **Expected:** Submenu rebuilds on open, listing "(All system
   audio)" plus current apps (playing apps first, marked "— playing").
2. User picks an app. **Expected:** Status "Capturing audio from <app>."; if already listening,
   the session restarts on the new source.

### Path: Switch to Windows speech

1. Audio ▸ Engine ▸ Windows speech. **Expected:** If System audio was selected it flips to
   Microphone (SAPI is mic-only); status explains the engine; Application and Model menu items
   disable.

### Error path: no microphone (SAPI, mic mode)

1. `Ctrl+R` with no mic. **Expected:** Error state; status announces "No microphone was found…"
   as an **interrupting** message so the screen reader speaks it immediately.

---

## 7. Accessibility Checklist

- **AutomationProperties.Name** — Transcript ListBox = "Transcript"; each item's Name is bound
  to its caption `Text` (so the screen reader reads the caption, not the type name); status is
  "Status"; the interim block is "Live caption in progress". "Now hearing" and "Transcript"
  labels are Heading Level 2.
- **Announcements** — Finalized captions: `AutomationNotificationProcessing.All` (queue in
  order), activity id `captions`, only when the "Announce" toggle is on. Status/errors:
  `MostRecent` (supersede), activity id `status`. No announcement on interim change.
- **Focus model** — Focus starts and stays on the transcript. When focus reaches the list
  container itself, it is pushed onto an item so arrows work immediately. `Tab`/`Shift+Tab`
  are swallowed; when the transcript is empty, arrows are swallowed too so focus can't wander.
  Menu bar is reached with `Alt`/`F10`, `IsTabStop=False`, `TabNavigation=None`.
- **Live region** — Status text is `LiveSetting="Polite"` as a visual/AT backstop in addition
  to the notification events.
- **Color** — No information conveyed by color alone. All brushes are system colors, so High
  Contrast is automatic. Focus visual is a 3 px system-highlight rectangle for sighted keyboard
  users.
- **Text scaling** — Caption text 14–48 pt via `Ctrl +/−`; app is PerMonitorV2 DPI-aware.

---

## 8. Acceptance Walkthrough (run in the app)

Set `LIVECAPTIONS_SEED=1` to prefill 8 sample lines for keyboard testing without live audio.

### Scenario: Review with a screen reader
**Setup:** Narrator/NVDA on; app running.
1. Start listening; speak or play audio. **Verify:** finalized lines appear and are spoken
   once each, in order.
2. Press `F8`. **Verify:** status says announcements are off; new lines stop being spoken.
3. Arrow through the transcript. **Verify:** each line reads its caption text; focus is not
   yanked when new lines arrive.

### Scenario: Low vision
1. `Ctrl +` several times. **Verify:** caption + interim text grow, capped at 48 pt; status
   announces the point size.
2. Turn on Windows High Contrast. **Verify:** the app recolors to the HC palette immediately.

### Scenario: Per-app capture
1. Play audio in two apps; select one under Audio ▸ Application. **Verify:** only that app's
   audio is captioned.

### Scenario: Engine constraint
1. Select Windows speech while System audio is active. **Verify:** source auto-flips to
   Microphone; Application/Model menus disable.

### Scenario: Save / copy
1. `Ctrl+Shift+C`, then `Ctrl+S`. **Verify:** clipboard holds the whole transcript; the saved
   `.txt` matches, with timestamps iff the toggle is on.

---

## 9. Success Metrics

- A blind user can start captions, hear each finalized line once, silence announcements with
  `F8`, and review the full history with the arrow keys — keyboard only.
- A low-vision user gets 48 pt captions and correct High Contrast colors with no code change.
- Whisper captions **system audio** (not just mic) and runs comfortably real-time on a
  Snapdragon X with the Base model.
- Swapping the engine or the audio source touches only one seam; the UI is unchanged.

---

## 10. Implementation Phases

1. **Seams & UI shell.** Define `IAudioCaptureSource` and `ICaptionSource`; build MainWindow
   (menu bar, live region, transcript ListBox, focus/keyboard rules). Deliverable: navigable
   empty transcript with `LIVECAPTIONS_SEED`. Risk: focus/Tab rules — verify with the §6 walkthrough.
2. **SAPI engine.** `SystemSpeechCaptionSource` end-to-end (mic → interim/final → transcript →
   announcements). Deliverable: working captions with the zero-download engine.
3. **WASAPI capture + Whisper.** `WasapiCaptureSource` (mono downmix + resample), mic and
   render-loopback subclasses, `WhisperModelStore` (download-with-progress), `WhisperCaptionSource`
   (VAD segmentation). Deliverable: on-device captions of mic and system audio. Risk: segmentation
   tuning (thresholds in §11).
4. **Per-app capture.** `ProcessLoopbackWaveIn` (COM interop, MTA thread) + the Application menu.
   Risk: COM marshalling/deadlock — MTA thread is the mitigation.
5. **Model management + polish.** Model picker, Download all / Clear, text scaling, timestamps,
   copy/save. Then the build + release workflows.

---

## 11. Tunable Constants (reference)

In `WhisperCaptionSource` (16 kHz sample rate):

| Constant | Value | Meaning |
|---|---|---|
| `VoiceRmsThreshold` | 0.012 | RMS above which a chunk counts as voiced |
| `SilenceToFinalizeSamples` | 0.6 s | Trailing silence that ends an utterance |
| `MinVoicedSamples` | 0.3 s | Minimum real speech before a final is emitted |
| `MaxUtteranceSamples` | 12 s | Hard cap that forces a finalize |
| `PartialIntervalMs` | 700 ms | How often the interim caption re-transcribes |

Whisper is built with `.WithLanguage("en")`. Bracketed non-speech tokens (e.g. `[BLANK_AUDIO]`)
are dropped. Model files live in `%LOCALAPPDATA%\AccessibleLiveCaptions\models`, downloaded from
`huggingface.co/ggerganov/whisper.cpp` as `ggml-<name>.bin`.

In `SherpaCaptionSource` (endpoint rules are sherpa-onnx's, in seconds of trailing silence):

| Constant | Value | Meaning |
|---|---|---|
| `Rule1MinTrailingSilence` | 2.4 s | Reset after silence when nothing was decoded |
| `Rule2MinTrailingSilence` | 0.9 s | Finalize a line after speech goes quiet |
| `Rule3MinUtteranceLength` | 15 s | Force-finalize very long utterances |
| Silence top-up threshold | 0.1 s | Wall-clock deficit before synthetic silence is fed |
| Stop-flush tail padding | 0.8 s | Right-context so the last word decodes on Stop |

Streaming model files (`encoder.onnx`, `decoder.onnx`, `joiner.onnx`, `tokens.txt`, 456 MB
total) live in `%LOCALAPPDATA%\AccessibleLiveCaptions\models\nemo-streaming-en-480ms`,
downloaded from `huggingface.co/csukuangfj/sherpa-onnx-nemo-streaming-fast-conformer-transducer-en-480ms`.

---

## 12. Files (build-it-again map)

| File | Responsibility |
|---|---|
| `AccessibleLiveCaptions.csproj` | net8.0-windows, WPF, `RuntimeIdentifier=win-arm64`, `<Version>`; refs System.Speech, NAudio, Whisper.net(.Runtime) |
| `app.manifest` | Win 10/11 compat + PerMonitorV2 DPI |
| `App.xaml(.cs)` | Application entry; `StartupUri=MainWindow.xaml` |
| `MainWindow.xaml(.cs)` | Accessible UI, commands/shortcuts, menu logic, engine/source wiring, model commands |
| `TranscriptLine.cs` | One finalized caption; `INotifyPropertyChanged` for the timestamp toggle |
| `Accessibility/ScreenReader.cs` | UIA Notification-event announcer |
| `Accessibility/ScreenReaderDetector.cs` | Detects the running screen reader for audio exclusion |
| `Audio/IAudioCaptureSource.cs` | Capture seam + `AudioFrameEventArgs` |
| `Audio/WasapiCaptureSource.cs` | Base capture (downmix + resample) + mic/loopback/process subclasses |
| `Audio/ProcessLoopbackWaveIn.cs` | Process-loopback `IWaveIn` via COM interop on an MTA thread |
| `Audio/ProcessLoopbackWatchdog.cs` | Detects a dead process-loopback tap (silent capture vs. audible sessions) |
| `Speech/ICaptionSource.cs` | Caption seam + state/text event args |
| `Speech/SystemSpeechCaptionSource.cs` | SAPI dictation engine (mic only) |
| `Speech/WhisperCaptionSource.cs` | Whisper engine + real-time segmentation |
| `Speech/WhisperModelStore.cs` | Model paths, presence, download-with-progress, clear |
| `Speech/SherpaCaptionSource.cs` | True-streaming engine (sherpa-onnx NeMo transducer) |
| `Speech/SherpaModelStore.cs` | Streaming model files: paths, download, clear |
| `Speech/WindowsAiCaptionSource.cs` | Windows AI flavor only: OS recognizer (NPU) engine |
| `packaging/Package.appxmanifest` | MSIX manifest for the Windows AI flavor (`systemAIModels`) |
| `packaging/build-windows-ai.ps1` | Publish + register the Windows AI flavor locally (dev mode) |
| `.github/workflows/build.yml` | On-demand build (workflow_dispatch), uploads the publish artifact |
| `.github/workflows/release.yml` | On `v*` tag: verify version, publish self-contained zip, create the GitHub Release |

---

## 13. Known Risks & Open Questions

| Risk | Prob. | Impact | Mitigation |
|---|---|---|---|
| COM deadlock activating process loopback | Med | Blocker (per-app only) | Dedicated MTA capture thread; 5 s activation timeout |
| Segmentation cuts words / lags | Med | Major | Tunable constants (§11); Base model default; larger models opt-in |
| Whisper model download fails offline | Low | Major | Clear status messages; SAPI works with no download |
| Process-loopback tap silent on some (offloaded) audio devices | Med | Major (exclude + per-app) | `ProcessLoopbackWatchdog`: auto-fallback to plain loopback (exclude) / spoken warning (per-app) |
| Framework-dependent artifact won't run | Low | Major | Release publishes **self-contained** win-arm64 |

**Open questions (deferred):** GA of `Microsoft.Windows.AI.Speech` (currently experimental;
revisit when it reaches a stable Windows App SDK so the NPU engine can ship in the default
build); a punctuation model for the Streaming engine; multi-architecture binaries;
transcript persistence across runs.

---

## 14. Future Directions

- **Ship the Windows on-device (NPU) engine by default** once `Microsoft.Windows.AI.Speech`
  graduates from the experimental Windows App SDK channel — the engine and MSIX packaging
  already exist behind `-p:WindowsAI=true`.
- **Punctuation/casing for the Streaming engine** via a sherpa-onnx online punctuation model.
- **Punctuation / diarization / language switching** — all inside the engine class; the
  accessible UI is unaffected.
- **The point for Microsoft:** the built-in recognizer is fine — add the accessible surface
  (full scrollable screen-reader-navigable transcript, controllable announcements, adjustable
  text, system-audio capture). This spec is the proof it's straightforward.
