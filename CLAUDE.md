# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this project is

A WPF app (single window, no MVVM framework, code-behind) that transcribes the mic or
system audio on-device and presents captions in a **screen-reader-navigable transcript**.
The recognizer is deliberately swappable; **the accessible surface is the product**. Any
change that makes the transcript less reviewable is a regression even if the recognition
improves.

Deep background lives in [docs/ProjectStatus.md](docs/ProjectStatus.md) (state, release
pipeline, hardware traps — read this first when picking the project up) and
[docs/LiveCaptionSpec.md](docs/LiveCaptionSpec.md) (design rationale, tunable constants,
file-by-file map).

## Build and run

```powershell
dotnet build                      # portable flavor (default)
dotnet run
dotnet build -p:WindowsAI=true    # adds the NPU engine (see below)
powershell -ExecutionPolicy Bypass -File packaging\build-windows-ai.ps1   # publish + register MSIX flavor locally (needs Developer Mode)
```

`build.bat [Release|Debug]` is a double-clickable wrapper around `dotnet build`.

**There is no test project.** Verification is manual, aided by environment variables that
are no-ops when unset:

| Variable | Effect |
|---|---|
| `LIVECAPTIONS_SEED=1` | Prefills 8 sample transcript lines — test keyboard navigation with no audio |
| `LIVECAPTIONS_DIAG=<path>` | Appends engine states, partials, finals to a log. **Writes transcribed audio content to disk** — delete when done |
| `LIVECAPTIONS_AUTOSTART=whisper\|streaming\|windows-ai\|sapi` | Selects that engine and starts listening on launch |

For the packaged (MSIX) app set these as **User**-scope env vars — the shell activates it,
so it does not inherit a parent process's environment.

Targets **win-arm64 only** (`RuntimeIdentifier` is hardcoded in the csproj). Nothing here
runs on x64 yet; see ProjectStatus §10 for what parameterizing it requires.

## The two seams

The whole design is two interfaces. Adding an engine or a capture source must not touch
the UI, and the UI must not reach past these:

```
Audio/IAudioCaptureSource   — emits normalized 16 kHz mono float frames in [-1, 1]
  WasapiCaptureSource (base: downmix + WdlResampler) →
    MicrophoneCaptureSource, SystemAudioCaptureSource,
    ProcessAudioCaptureSource, SystemAudioExceptProcessCaptureSource

Speech/ICaptionSource       — Start/Stop + PartialRecognized / FinalRecognized / StateChanged
  WhisperCaptionSource, SherpaCaptionSource,
  WindowsAiCaptionSource (MSIX flavor only), SystemSpeechCaptionSource
```

`MainWindow.CreateSelectedSource()` is the only place the two are wired together. The
normalization contract (16 kHz / mono / float) is absolute — engines never resample.

`PartialRecognized` = still-changing hypothesis, shown but **never announced**.
`FinalRecognized` = settled line, appended to the transcript and announced.

## Accessibility invariants — do not break these

- **The transcript is a document.** Focus starts and stays on the transcript ListBox;
  `Tab`/`Shift+Tab` are swallowed. Every command is reachable only from the menu bar
  (`Alt`/`F10`). Do not add tab stops or toolbars.
- **Never flood the screen reader.** Announcements go through
  `Accessibility/ScreenReader.Announce` (UIA Notification event, not focus moves).
  Finalized captions use `AutomationNotificationProcessing.All` + activity id `captions`;
  status/errors use `MostRecent` + activity id `status`. Interim text is never announced.
  The announce toggle (`F8`) is user-controlled.
- **System colors only** — no hardcoded brushes, so High Contrast works for free.
- Each finalized caption is its own focusable list item with its `AutomationProperties.Name`
  bound to the caption text.

Full checklist: LiveCaptionSpec §7.

## The WindowsAI flavor

`-p:WindowsAI=true` switches the TFM to `net10.0-windows10.0.26100.0`, defines
`WINDOWS_AI`, and compiles in `Speech/WindowsAiCaptionSource.cs` — which the default build
`<Compile Remove>`s entirely, so the portable build has zero Windows App SDK dependency.
The API only functions when MSIX-packaged with the `systemAIModels` capability, so the
engine's menu item is disabled (with a HelpText reason) in the portable build.

The locally-registered dev copy shares package identity with the released MSIX; if
`Add-AppxPackage` reports `ResourceExists`, run
`Get-AppxPackage -Name AccessibleLiveCaptions | Remove-AppxPackage` first.

## Engine quirks that are intentional — don't "fix" them

- `SherpaCaptionSource` feeds **wall-clock-paced synthetic silence** when loopback delivers
  no frames (WASAPI loopback goes fully silent when nothing renders, so the endpointer
  would never fire), and pads 0.8 s of silence before the final flush so the last word has
  right-context.
- sherpa-onnx NeMo transducers support **greedy search only**; `modified_beam_search` throws.
- The Streaming engine's output is lowercase and unpunctuated — a model limitation, not a UI bug.
- `Audio/ProcessLoopbackWatchdog.cs` deliberately *degrades* — some devices return only
  silent packets from process loopback, so after ~3 s of "reference audible, capture silent"
  it turns off screen-reader exclusion and falls back to plain loopback. Captions win over
  exclusion.
- Whisper/Sherpa tunable constants (VAD thresholds, endpoint rules) are documented with
  their rationale in LiveCaptionSpec §11 — change them there too.

## Releasing

`<Version>` in the csproj must match the pushed `v*` tag — `release.yml` verifies it and
fails otherwise. Release notes are read from `docs/release-notes-<tag>.md`, which must
exist before tagging. Run the workflow manually first (Actions ▸ Release ▸ Run workflow) as
a signing dry run; it signs everything and publishes nothing.

Signing is Azure Artifact Signing via GitHub OIDC (no stored certificate). The sharpest
trap: `packaging/Package.appxmanifest`'s `Identity Publisher` must be character-identical
to the certificate subject `CN=kelly ford, O=kelly ford, L=Madison, S=wi, C=US` — lowercase
as written. A mismatch fails with `0x8007000b`, and changing the string changes package
identity so Windows treats it as a different app. Details in ProjectStatus §6.
