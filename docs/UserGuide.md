# Accessible Live Captions — User Guide

Accessible Live Captions turns speech on your PC into a **full, reviewable transcript**
that works properly with screen readers and low vision. It captions your microphone or
anything playing on the computer — a meeting, a video, one specific app — entirely
**on-device**: no cloud, no account, no audio ever leaves your PC.

This guide covers everyday use. For the design and engineering details, see the
[specification](LiveCaptionSpec.md).

---

## 1. Getting started

### Requirements

- Windows 11 (built and tested on Snapdragon X / ARM64 Copilot+ PCs).
- No microphone is needed for system-audio captioning.
- An internet connection the **first** time you use the Whisper or Streaming engine
  (one-time model download). After that, everything is offline.

### Install and run

The Releases page offers two downloads. Both are code-signed.

**`AccessibleLiveCaptions-<version>-win-arm64.msix` — recommended.** This is the full
app, including the Windows on-device (NPU) engine. Double-click it and choose Install;
you do **not** need Developer Mode, and there is no certificate to install. It appears in
the Start menu afterwards and uninstalls from Settings like any other app.

To install it entirely from the keyboard — which avoids the App Installer dialog and
gives readable error text if anything goes wrong — run in PowerShell:

```powershell
Add-AppxPackage -Path .\AccessibleLiveCaptions-v0.4.0-win-arm64.msix
```

If the App Installer dialog sits at **0%** with no error message, that is Windows'
package-deployment queue being stuck, not a problem with the download. A restart clears
it. This install normally finishes in under a second, so there is no point waiting.

**`AccessibleLiveCaptions-<version>-win-arm64-portable.zip`.** No installation: unzip
anywhere and run `AccessibleLiveCaptions.exe`. Useful from a USB stick or where you can't
install software. It does not include the Windows on-device (NPU) engine, which Windows
only permits to installed apps.

The first time you run a downloaded app, Windows SmartScreen may still warn about it
until the release has been downloaded enough times to establish reputation. Choose
**More info**, then **Run anyway**.

If you build from source instead: `dotnet run` in the repository root.

### Your first captions, in three steps

1. Start the app. Focus is already on the (empty) transcript, and the status line reads
   "Ready. Press Start listening (Control R) to begin."
2. Press **Ctrl+R**. The first time, the status line reports the model download and
   load progress; then it announces "Listening."
3. Play some audio — a video, a meeting, anything. Finished sentences are added to the
   transcript and (by default) spoken by your screen reader; the in-progress guess
   appears under "Now hearing" without being spoken.

Press **Ctrl+R** again to stop.

---

## 2. The window, in words

From top to bottom:

- **Menu bar** — Captions, Audio, and View menus. Reach it with `Alt` or `F10`; navigate
  with the arrow keys. It is not in the Tab order.
- **Transcript** — the main area and the only focusable control. Every finalized caption
  is one list item; review with the arrow keys at your own pace. New lines never steal
  focus while you are reviewing. (`F7` swaps this for the compact **panel** — see
  section 3.1. The captions are the same either way.)
- **Now hearing** — the live, still-changing guess for the current sentence. Shown, never
  announced, so your screen reader is not flooded with half-words.
- **Status line** — a live region that reports state changes ("Listening.", download
  progress, errors). Important messages are announced immediately.

## 3. Keyboard reference

| Action | Key |
| --- | --- |
| Start / stop listening | `Ctrl+R` |
| Review transcript lines | `↑` `↓` `Home` `End` |
| Open the menu bar | `Alt` or `F10` |
| Switch between transcript and panel | `F7` |
| Toggle announcements of new captions | `F8` |
| Larger / smaller caption text | `Ctrl` `+` / `Ctrl` `−` |
| Copy the whole transcript | `Ctrl+Shift+C` |
| Save the transcript to a text file | `Ctrl+S` |
| Clear the transcript | `Ctrl+L` |

In **panel mode** only (see section 3.1):

| Action | Key |
| --- | --- |
| Previous / next caption | `↑` `↓`, or `Alt+Up` / `Alt+Down` |
| Oldest caption | `Home` |
| Catch up with live captions | `End`, or `Alt+End` |

`Tab` deliberately does nothing: the captions are the only content, and all commands
live on the menus.

## 3.1 Two ways to see captions (View ▸ Presentation)

Press **F7** to switch, or choose **View ▸ Presentation**.

**Transcript** (the default) is the full history: every finalized caption is its own
list item, and you review the whole session with the arrow keys.

**Panel** shows one caption at a time in large text and hides the transcript. This is
how the built-in Windows Live Captions presents itself — compact and glanceable — and
it suits a large-text reader or a second screen.

Choosing the panel costs you nothing:

- **Every caption is still kept.** Switch back to Transcript and the whole session is
  there, from the first line. Nothing is discarded.
- **Your screen reader still reads new captions**, through exactly the same
  announcements the transcript uses. Panel mode sounds identical to transcript mode.
- **The panel keeps up on its own**, always moving to the newest caption.

### Reading back in the panel

`↑` and `↓` step to the previous and next caption; `Home` jumps to the oldest. There are
also **Previous** and **Next** buttons you can click.

Stepping back **pauses following**, so a newly arriving caption cannot pull you away
from the line you are reading — the same protection the transcript has. The readout on
the right tells you which state you are in:

- `Live — 24 captions` — following; new captions appear as they arrive.
- `Caption 6 of 24 — paused` — you have stepped back; the panel is holding still.

Press `End`, or the **Follow live** button, to jump to the newest caption and start
following again. Stepping forward onto the newest caption does the same thing.

The Previous, Next, and Follow live buttons are clickable and readable by assistive
technology, but they are not in the Tab order and never take focus, so `Tab` still does
nothing anywhere in the app.

---

## 4. Choosing what to caption (Audio ▸ Source)

| Source | What you get |
| --- | --- |
| **System audio (what you hear)** — default | Everything the PC plays: meetings, videos, any app |
| **Microphone** | You, or people in the room |

### Captioning a single app (Audio ▸ Application)

With System audio selected, the **Application** submenu lists running apps — apps
currently playing audio are listed first and marked "— playing". Pick one to caption
only that app (and its child processes); pick "(All system audio)" to go back to the
whole mix. The list refreshes every time the submenu opens.

This is the cleanest way to caption a meeting: nothing else on the system — music,
notification sounds, other tabs — can bleed into the transcript.

### Excluding your screen reader (Audio ▸ Exclude screen reader speech)

If you use a screen reader, its voice is part of "what you hear" — so whole-mix
captioning would normally interleave the meeting's captions with your screen reader's
narration, including its announcements of the captions themselves.

**Exclude screen reader speech**, on by default, removes the running screen reader's
audio (NVDA, JAWS, or Narrator) from whole-mix capture. The captions cover the meeting
or video; your reader can talk freely over it.

Notes:

- It applies only to "(All system audio)". When you caption a single app the toggle is
  disabled, because that app's audio never contains your reader anyway.
- One screen reader is excluded — the first found, preferring NVDA/JAWS over Narrator.
- If you restart your screen reader while listening, press `Ctrl+R` twice (stop, start)
  so the exclusion picks up the reader's new process.

---

## 5. Choosing an engine (Audio ▸ Engine)

All engines run entirely on your PC. They differ in how fast words appear, how the text
is formatted, and what they can listen to.

| Engine | Words appear | Text style | Sources | Download |
| --- | --- | --- | --- | --- |
| **Whisper** (default) | After each phrase (~1 s behind) | Punctuation and casing | Mic + system | 75 MB – 2.9 GB (model choice) |
| **Streaming** | Word by word (~½ s behind) | Lowercase, no punctuation | Mic + system | 456 MB once |
| **Windows on-device (NPU)** | After each phrase, fast | Punctuation and casing | Mic + system | None (model ships with Windows) |
| **Windows speech (SAPI)** | Word by word, instant | Plain | **Mic only** | None |

Recommendations:

- **Want the words as fast as possible** (following a fast conversation): **Streaming**.
  You trade punctuation for the lowest delay and very light CPU use.
- **Want the nicest transcript to review or save**: **Whisper** — pick a larger model
  under Audio ▸ Whisper model if your machine keeps up (see below).
- **On a Copilot+ PC with the packaged version installed**: **Windows on-device** is the
  best of both — punctuated text, fast, and it runs on the NPU so it uses essentially no
  CPU or battery. See section 8.
- **No downloads, right now, mic only**: Windows speech.

### Whisper model choice (Audio ▸ Whisper model)

Applies to the Whisper engine only. Tiny and Base are fast; Small is noticeably more
accurate and still real-time on a Snapdragon X; Medium and above trade lag for accuracy.
Each model downloads once, on first use.

### Managing downloads

- **Audio ▸ Download all models** fetches every Whisper model plus the Streaming model
  (about 7.5 GB total) — useful before going offline.
- **Audio ▸ Clear downloaded models** deletes them all. Models live in
  `%LOCALAPPDATA%\AccessibleLiveCaptions\models`.

---

## 6. Announcements, text size, timestamps (View menu)

- **Announce new captions** (`F8`, on by default): each finalized line is spoken once,
  in order, via UI Automation notifications — no focus stealing. Turn it off to read
  the transcript yourself; captions keep accumulating silently.
- **Larger / smaller text** (`Ctrl +` / `Ctrl −`): caption text from 14 to 48 points.
  The app uses your system colors, so High Contrast themes apply automatically.
- **Show timestamps**: prefixes each line with `[HH:mm:ss]`, and includes timestamps
  when you save or copy.

## 7. Keeping the transcript

- **Copy all** (`Ctrl+Shift+C`) puts the entire transcript on the clipboard.
- **Save transcript** (`Ctrl+S`) writes a `.txt` file (timestamps included if shown).
- The transcript is not saved automatically; it is gone when you close the app.

---

## 8. The Windows on-device (NPU) engine

The packaged (MSIX) version of the app adds a fourth engine: the same on-device
recognizer Windows uses for its own Live Captions, exposed through the Windows AI
Speech API. On Copilot+ PCs it runs on the NPU — captions cost essentially no CPU or
battery — and its output has full punctuation and casing. It is currently
English-only, and the underlying Windows API is still in preview.

Requirements: Windows 11 24H2 or later; a Copilot+ PC for NPU speed (other PCs run the
same engine on CPU after a one-time model download by Windows Update).

**To get it: install the `.msix` download** (see section 1), then choose
Audio ▸ Engine ▸ **Windows on-device — NPU**. Windows only grants access to this engine
to installed apps, so the portable zip shows the menu item disabled, with the reason in
its tooltip.

To build it yourself from source instead, turn on **Developer Mode**
(Settings ▸ System ▸ For developers) and run
`powershell -ExecutionPolicy Bypass -File packaging\build-windows-ai.ps1`
from the repository root.

---

## 9. Troubleshooting

**No captions appear.**
Check the status line first — it says what the app is doing. Then check the source:
for System audio, something must actually be playing; for a specific app under
Audio ▸ Application, that app must be the one making sound. If your screen reader's
speech is the only audio playing, remember it is excluded by default.

**"Now hearing" updates but lines never move into the transcript.**
Lines finalize after a short silence. Continuous audio (music under speech, back-to-back
talk) can delay finalization; Whisper and Streaming both force a line after ~12–15 s.

**The first start takes a while.**
The Whisper/Streaming engines download their model on first use (the status line reports
progress), then load it into memory. Later starts skip the download.

**Model download fails.**
You are probably offline. Windows speech (SAPI) works with no download, microphone only.

**Captions contain my screen reader's speech.**
Audio ▸ **Exclude screen reader speech** should be checked. If you restarted your reader
mid-session, stop and start listening again. Only one reader is excluded at a time.

**The app announced that my audio device does not support excluding the screen reader.**
Some audio devices (seen with certain "Hi-Res" headphone outputs) render audio in a way
Windows' per-process capture cannot tap. The app detects this within a few seconds,
turns exclusion off, and keeps captioning everything — including your reader's speech.
Per-application capture has the same limitation on such devices, and the app announces
that too. To get exclusion or per-app capture back, switch Windows' default output to a
different device (for example the built-in speakers) and restart listening.

**Captions show things like "soft music" but not the dialog.**
That was the recognizer hearing silence and guessing. Current versions detect the cause
(see the previous item) and fix themselves; if you see it, stop and start listening.

**The Windows on-device engine is disabled.**
It needs the packaged version (section 8), Windows 11 24H2+, and on non-Copilot+ PCs a
one-time model download that Windows performs in the background.

**Streaming captions are all lowercase.**
That's inherent to this engine's model. Choose Whisper or Windows on-device when you
want punctuation.

## 10. Privacy

All recognition happens on your PC. The app makes network requests only to download
model files from Hugging Face (Whisper, Streaming) when you first select those engines
or use "Download all models". Transcripts exist only in the app window until you copy
or save them, and are discarded on exit.
