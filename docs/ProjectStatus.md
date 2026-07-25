# Project Status & Agent Handoff

> Written for a person or AI agent picking this project up cold — especially on a
> **different machine** where it has never run. It records what the project is, what
> state it is in, how to build and release it, and the hardware-specific traps that
> have already cost time. Last updated for **v0.3.0** (July 2026).
>
> Companion documents: [UserGuide.md](UserGuide.md) for end users,
> [LiveCaptionSpec.md](LiveCaptionSpec.md) for the design rationale and a
> file-by-file map.

---

## 1. What this is, in one paragraph

Windows ships Live Captions, but its presentation is hostile to screen-reader and
low-vision users: a sliver of text, no reviewable history, effectively invisible to
Narrator/NVDA/JAWS. **Accessible Live Captions** is a small WPF app that fixes the
*presentation*: it transcribes the microphone or any audio playing on the PC,
on-device, and puts every finalized line into a full, scrollable, keyboard-navigable
transcript where each line is its own focusable, screen-reader-readable item. The
recognizer is deliberately swappable; the accessible surface is the product. The
audience is blind and low-vision users, and the intended secondary audience is
Microsoft — it is a proof that this is straightforward on a Copilot+ PC today.

## 2. State at a glance

| | |
|---|---|
| Repo | `kellylford/LiveCaptionsWithAccessibility` |
| Current release | **v0.3.0** — signed MSIX + signed portable zip |
| Runtime | **.NET 10 LTS** (built with SDK 10.0.302) |
| Architecture | **win-arm64 only** — see §7, this matters a lot |
| UI | WPF, single window, menu-bar driven |
| Engines | 4 (see §4) |
| Code signing | Azure Artifact Signing via GitHub OIDC — working, verified |
| Distribution | GitHub Releases; MSIX installs by double-click, no Developer Mode |

Everything below has been exercised on a Snapdragon X Elite (X1E78100), Windows 11
build 26300, with JAWS running.

## 3. Repo orientation

The design has **two seams**, and they are the thing to understand first:

```
Audio/IAudioCaptureSource   — emits normalized 16 kHz mono float frames
  MicrophoneCaptureSource, SystemAudioCaptureSource,
  ProcessAudioCaptureSource, SystemAudioExceptProcessCaptureSource

Speech/ICaptionSource       — Start/Stop + PartialRecognized/FinalRecognized/StateChanged
  WhisperCaptionSource, SherpaCaptionSource,
  WindowsAiCaptionSource (MSIX-only flavor), SystemSpeechCaptionSource
```

The UI depends only on `ICaptionSource`; the engines depend only on
`IAudioCaptureSource`. Adding an engine or a capture source should not touch the UI.

Everything else worth knowing is in [LiveCaptionSpec.md](LiveCaptionSpec.md) §12,
which maps every file to its responsibility.

## 4. The four engines

| Engine | Backing | Notes |
|---|---|---|
| **Whisper** (default) | Whisper.net 1.9.1 (whisper.cpp), CPU | Chunked: re-transcribes the utterance every ~700 ms. Punctuated. Model picker Tiny→Large v3, downloaded on first use. |
| **Streaming** | sherpa-onnx 1.13.4, NeMo FastConformer transducer (480 ms export), CPU | True streaming, word-by-word ~0.5 s behind, RTF ≈ 0.10. **Lowercase, no punctuation.** 456 MB model on first use. |
| **Windows on-device (NPU)** | `Microsoft.Windows.AI.Speech` (Windows App SDK 2.2-experimental) | The OS Live Captions recognizer. Runs on the Hexagon NPU on Copilot+. Punctuated. **Only exists in the MSIX flavor** — Windows grants the API to packaged apps only. English only. |
| **Windows speech (SAPI)** | System.Speech 10.0.10 | Instant, zero download, **microphone only**. |

Two engine-specific quirks already handled in code, don't "fix" them again:

- `SherpaCaptionSource` feeds **wall-clock-paced silence** when loopback delivers no
  frames, because WASAPI loopback goes completely silent when nothing is rendering and
  the endpointer would otherwise never fire. It also pads 0.8 s of silence before the
  final flush so the last word has right-context to decode.
- NeMo transducers in sherpa-onnx support **greedy search only**; `modified_beam_search`
  throws.

## 5. Building

```powershell
dotnet build                                    # portable flavor
dotnet build -p:WindowsAI=true                  # adds the NPU engine
```

`-p:WindowsAI=true` switches the TFM to `net10.0-windows10.0.26100.0` for WinRT
projections and compiles in `Speech/WindowsAiCaptionSource.cs`. The default build
excludes that file entirely, so the portable build has no Windows App SDK dependency.

To build **and locally register** the packaged flavor (requires Developer Mode):

```powershell
powershell -ExecutionPolicy Bypass -File packaging\build-windows-ai.ps1
```

That publishes to `bin\windows-ai\publish` and registers *that folder* as an app —
nothing is copied. Do not delete the folder while it is registered.

⚠️ The locally registered copy has the **same package identity** as the released MSIX,
so the two cannot coexist. If `Add-AppxPackage` fails with `ResourceExists`, uninstall
the other one first:

```powershell
Get-AppxPackage -Name AccessibleLiveCaptions | Remove-AppxPackage
```

Package identity, for reference: family name `AccessibleLiveCaptions_mzky7tc8qkasw`,
AUMID `AccessibleLiveCaptions_mzky7tc8qkasw!App`. Launch the installed copy with:

```powershell
explorer.exe "shell:AppsFolder\AccessibleLiveCaptions_mzky7tc8qkasw!App"
```

## 6. Release and signing pipeline

`.github/workflows/release.yml` builds and signs both artifacts. It runs on a pushed
`v*` tag, **or manually as a dry run** (Actions ▸ Release ▸ Run workflow) which signs
everything, prints the resulting signature status, attaches the artifacts to the run,
and publishes nothing. Use the dry run before any real release.

How signing works — no certificate or long-lived secret exists anywhere:

1. The job runs in the GitHub **environment `azure-signing`** (no protection rules —
   it exists purely so the OIDC token's subject matches).
2. `azure/login@v3` exchanges the workflow's OIDC token for Azure credentials using
   repo secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
3. `azure/artifact-signing-action@v2` signs via Azure Artifact Signing —
   endpoint `https://eus.codesigning.azure.net/`, account `kellylford`,
   certificate profile `kellyford-public`.

The Azure side is an app registration named **`github-artifact-signing`**, which holds
one federated credential per repo. This repo has two, matching the established
pattern: a plain subject and an "immutable" one using GitHub's numeric IDs so a repo
rename does not break signing.

```
repo:kellylford/LiveCaptionsWithAccessibility:environment:azure-signing
repo:kellylford@44002405/LiveCaptionsWithAccessibility@1292520798:environment:azure-signing
```

**Order matters and is deliberate:** payload executables are signed *before*
`makeappx pack`, and the `.msix` is signed *after*, because the package signature
covers the whole layout and any later edit invalidates it.

**Timestamping is mandatory, not optional.** Artifact Signing certificates are
short-lived (reissued daily, valid ~3 days). Without `timestamp-rfc3161` the
signatures stop validating within days.

### The single sharpest trap

`Package.appxmanifest`'s `Identity Publisher` must be **character-identical** to the
signing certificate's subject:

```
CN=kelly ford, O=kelly ford, L=Madison, S=wi, C=US
```

A mismatch fails signing with `0x8007000b`. Note it is lowercase — that is what the
certificate says. Changing this string changes the package identity hash, which makes
Windows treat it as a *different app* (this is exactly why the family-name hash is
`mzky7tc8qkasw` and not something older).

## 7. Installing on a fresh Copilot+ PC — read before troubleshooting

**First, check the CPU architecture. This is the most likely failure.** The release is
**ARM64 only**. On an Intel or AMD Copilot+ PC (Lunar Lake, Strix Point) the MSIX will
refuse to install and the portable exe produces a bare *"This app can't run on your
PC"* dialog with no explanation.

```powershell
$env:PROCESSOR_ARCHITECTURE   # expect ARM64
```

If it says AMD64, stop — you need an x64 build, which does not exist yet (§9).

Then, in order:

| Symptom | Cause / fix |
|---|---|
| SmartScreen warns on download or first run | Expected. The release is signed, but reputation accrues per-publisher over many downloads; Microsoft says weeks and hundreds of installs. Choose **More info** → **Run anyway**. |
| MSIX won't install, `ResourceExists` | Another copy with the same identity is registered. `Get-AppxPackage -Name AccessibleLiveCaptions \| Remove-AppxPackage` |
| MSIX won't install, untrusted / certificate error | The signature or its timestamp is broken. Verify: `Get-AuthenticodeSignature .\file.msix` should report `Valid`. |
| Prefer no dialog / keyboard-only install | `Add-AppxPackage -Path .\AccessibleLiveCaptions-v0.3.0-win-arm64.msix` — gives real error text instead of a modal. |
| "Windows on-device — NPU" menu item disabled | Its tooltip/HelpText states the reason. Needs the **MSIX** build (not portable) and Windows 11 24H2+ (build 26100). |
| NPU engine hangs at "Loading the Windows on-device recognizer" | Normal on first use — allow **up to ~60 s**. On non-Copilot+ hardware Windows downloads the model via Windows Update first, which is slower. |
| Long pause on first start of Whisper/Streaming | One-time model download. Status line reports progress. Streaming is 456 MB. |
| Captions say "soft music", "keyboard clicking", "(gentle music)" with no real text | The recognizer is being fed **digital silence** and hallucinating. Almost always the dead-tap problem — see §8. |

## 8. Hardware-dependent behavior that has already burned time

**Some audio devices silently break per-process audio capture.** Observed on a
"Hi-Res Audio" headphone output (likely hardware-offloaded rendering): the Windows
process-loopback API returns only silent packets in **both** include and exclude
modes, while ordinary device loopback hears everything fine. Because the capture
"works" (silent packets flow), the only symptom is a transcript of hallucinated
sound-tags.

`Audio/ProcessLoopbackWatchdog.cs` detects this by comparing captured energy against
the peak meters of the audio sessions that *should* be reaching the capture (screen-
reader speech processes are filtered out of the reference so the reader talking alone
cannot false-trigger). After ~3 s of "reference audible, capture silent" it:

- in **exclude** mode: announces the problem, unchecks *Exclude screen reader speech*,
  and restarts on plain loopback — captions win over exclusion;
- in **per-app** mode: announces that the device cannot do per-app capture and suggests
  *(All system audio)*.

If a new machine shows this, it is a device limitation, not a regression. Switching
the default output device (e.g. to built-in speakers) restores per-process capture.

**The screen reader is part of "what you hear."** On a target user's machine the
dominant voice in system audio is their own screen reader. *Audio ▸ Exclude screen
reader speech* is on by default for this reason. When testing, remember that anything
JAWS/NVDA says will otherwise end up in the transcript — including the app's own
caption announcements, which is a feedback loop.

## 9. Verified vs. not

Verified on the reference machine at v0.3.0, on .NET 10:

- Streaming engine — reference sentences transcribed exactly, with screen-reader exclusion active
- Whisper 1.9.1 — transcribed with punctuation
- Windows on-device NPU — punctuated captions ~29 s after launch, packaged flavor
- Both flavors build with zero warnings
- Signing pipeline end-to-end; the **published** MSIX downloaded fresh reports `Valid` and installs

Not verified:

- **The SAPI engine** — not separately exercised on .NET 10 (thin wrapper over the OS recognizer, no native assets of its own)
- **Any non-ARM64 machine** — no x64 build exists
- **Any machine other than the reference Snapdragon X Elite** — this is precisely what a second Copilot+ PC would establish

## 10. Open items

1. **x64 support.** Today every Intel/AMD visitor hits a dead end. An x64 build was
   proven to work: it needs `RuntimeIdentifier` parameterized and the
   architecture-pinned native package followed along — `org.k2fsa.sherpa.onnx.runtime.win-arm64`
   must become `...win-x64` for x64 (a `$(RuntimeIdentifier)`-substituted reference
   works). Then matrix the release workflow. Whisper.net and NAudio are fine.
2. **Punctuation for the Streaming engine** — sherpa-onnx offers an online punctuation
   model that would remove its main drawback.
3. **Ship the NPU engine in the default build** once `Microsoft.Windows.AI.Speech`
   leaves the experimental Windows App SDK channel. The engine and packaging already
   exist; only the channel is blocking.
4. **Auto-update** — a hosted `.appinstaller` file at a stable URL would let installed
   copies update themselves. Note MSIX has no delta updates; each update is a full
   ~28 MB download.
5. A stray federated credential named `gh-weatherfast-plain` on the Azure app
   registration has a malformed subject (`repo:azure-signing`) and matches nothing.
   Harmless, worth deleting.

## 11. Test aids built into the app

Environment variables, all no-ops when unset:

| Variable | Effect |
|---|---|
| `LIVECAPTIONS_SEED=1` | Prefills 8 sample transcript lines so keyboard navigation can be tested with no audio |
| `LIVECAPTIONS_DIAG=<path>` | Appends engine states, partials, and finals to a log file |
| `LIVECAPTIONS_AUTOSTART=<whisper\|streaming\|windows-ai\|sapi>` | Selects that engine and starts listening on launch |

⚠️ `LIVECAPTIONS_DIAG` writes **transcribed audio content** to disk. On a real user's
machine that can include whatever their screen reader was reading. Delete the log when
finished.

For the packaged app, set these as **User**-scope environment variables before
launching — the app is activated by the shell, so it does not inherit a parent
process's environment.

A useful end-to-end pattern that avoids needing a human to speak: drive
`System.Speech.Synthesis.SpeechSynthesizer` to play known sentences through the
speakers, capture with a loopback source, and assert on the resulting captions.
