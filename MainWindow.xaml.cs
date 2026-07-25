using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AccessibleLiveCaptions.Accessibility;
using AccessibleLiveCaptions.Audio;
using AccessibleLiveCaptions.Speech;
using Microsoft.Win32;
using Whisper.net.Ggml;

namespace AccessibleLiveCaptions;

public partial class MainWindow : Window
{
    // ---- Commands (with their keyboard gestures baked in) ---------------------
    public static readonly RoutedUICommand ToggleListenCommand =
        new("Start or stop listening", nameof(ToggleListenCommand), typeof(MainWindow),
            [new KeyGesture(Key.R, ModifierKeys.Control)]);

    public static readonly RoutedUICommand ClearCommand =
        new("Clear transcript", nameof(ClearCommand), typeof(MainWindow),
            [new KeyGesture(Key.L, ModifierKeys.Control)]);

    public static readonly RoutedUICommand CopyAllCommand =
        new("Copy all", nameof(CopyAllCommand), typeof(MainWindow),
            [new KeyGesture(Key.C, ModifierKeys.Control | ModifierKeys.Shift)]);

    public static readonly RoutedUICommand SaveCommand =
        new("Save transcript", nameof(SaveCommand), typeof(MainWindow),
            [new KeyGesture(Key.S, ModifierKeys.Control)]);

    public static readonly RoutedUICommand IncreaseFontCommand =
        new("Larger text", nameof(IncreaseFontCommand), typeof(MainWindow),
            [new KeyGesture(Key.OemPlus, ModifierKeys.Control), new KeyGesture(Key.Add, ModifierKeys.Control)]);

    public static readonly RoutedUICommand DecreaseFontCommand =
        new("Smaller text", nameof(DecreaseFontCommand), typeof(MainWindow),
            [new KeyGesture(Key.OemMinus, ModifierKeys.Control), new KeyGesture(Key.Subtract, ModifierKeys.Control)]);

    public static readonly RoutedUICommand ToggleAnnounceCommand =
        new("Toggle screen-reader announcements", nameof(ToggleAnnounceCommand), typeof(MainWindow),
            [new KeyGesture(Key.F8)]);

    public static readonly RoutedUICommand TogglePresentationCommand =
        new("Switch between transcript and panel", nameof(TogglePresentationCommand), typeof(MainWindow),
            [new KeyGesture(Key.F7)]);

    // Panel navigation. Alt+arrow rather than a bare arrow because in transcript mode
    // the arrows belong to the list; a bare-arrow InputGesture would hijack it. Bare
    // arrows still work in panel mode — see the PreviewKeyDown handler, where nothing
    // else is competing for them.
    public static readonly RoutedUICommand PanelPreviousCommand =
        new("Previous caption", nameof(PanelPreviousCommand), typeof(MainWindow),
            [new KeyGesture(Key.Up, ModifierKeys.Alt)]);

    public static readonly RoutedUICommand PanelNextCommand =
        new("Next caption", nameof(PanelNextCommand), typeof(MainWindow),
            [new KeyGesture(Key.Down, ModifierKeys.Alt)]);

    public static readonly RoutedUICommand FollowLiveCommand =
        new("Follow live captions", nameof(FollowLiveCommand), typeof(MainWindow),
            [new KeyGesture(Key.End, ModifierKeys.Alt)]);

    // ---- State ----------------------------------------------------------------
    private readonly ObservableCollection<TranscriptLine> _lines = new();
    private ICaptionSource? _captions;
    private double _captionFontSize = 22;
    private const double MinFont = 14;
    private const double MaxFont = 48;

    // Panel presentation: the transcript is hidden and one caption is shown at a time.
    // _lines is still the single source of truth, so switching back restores everything.
    // _panelIndex is which line the panel is showing; _followLive means "stay on the
    // newest", which is the default so the panel keeps up with the audio unattended.
    private bool _panelMode;
    private int _panelIndex = -1;
    private bool _followLive = true;

    // Selected application for per-app system-audio capture (null = whole system mix).
    private int? _selectedAppPid;
    private string _selectedAppName = "";

    // Whisper model: speed vs. accuracy trade-off.
    private GgmlType _selectedModel = GgmlType.BaseEn;

#if WINDOWS_AI
    // Windows AI flavor only: the OS on-device recognizer (NPU on Copilot+ PCs),
    // created at runtime so the XAML stays identical across flavors.
    private MenuItem? _engWindowsAi;
#endif

    public MainWindow()
    {
        InitializeComponent();

        TranscriptList.ItemsSource = _lines;

        // Test aid: set LIVECAPTIONS_SEED=1 to prefill the transcript so keyboard
        // navigation can be exercised without a live audio source. No effect otherwise.
        if (Environment.GetEnvironmentVariable("LIVECAPTIONS_SEED") == "1")
        {
            for (int i = 1; i <= 8; i++)
                _lines.Add(new TranscriptLine($"Sample caption line number {i}.",
                    DateTime.Now.ToString("HH:mm:ss"), ShowTimestamps));
        }

        CommandBindings.Add(new CommandBinding(ToggleListenCommand, (_, _) => ToggleListen()));
        CommandBindings.Add(new CommandBinding(ClearCommand, (_, _) => ClearTranscript()));
        CommandBindings.Add(new CommandBinding(CopyAllCommand, (_, _) => CopyAll(), (_, e) => e.CanExecute = _lines.Count > 0));
        CommandBindings.Add(new CommandBinding(SaveCommand, (_, _) => SaveTranscript(), (_, e) => e.CanExecute = _lines.Count > 0));
        CommandBindings.Add(new CommandBinding(IncreaseFontCommand, (_, _) => AdjustFont(+2)));
        CommandBindings.Add(new CommandBinding(DecreaseFontCommand, (_, _) => AdjustFont(-2)));
        CommandBindings.Add(new CommandBinding(ToggleAnnounceCommand, (_, _) => MenuAnnounce.IsChecked = !MenuAnnounce.IsChecked));
        CommandBindings.Add(new CommandBinding(TogglePresentationCommand, (_, _) => SetPresentation(!_panelMode)));
        CommandBindings.Add(new CommandBinding(PanelPreviousCommand, (_, _) => StepPanel(-1),
            (_, e) => e.CanExecute = _panelMode && _panelIndex > 0));
        CommandBindings.Add(new CommandBinding(PanelNextCommand, (_, _) => StepPanel(+1),
            (_, e) => e.CanExecute = _panelMode && _panelIndex >= 0 && _panelIndex < _lines.Count - 1));
        CommandBindings.Add(new CommandBinding(FollowLiveCommand, (_, _) => FollowLive(),
            (_, e) => e.CanExecute = _panelMode && !_followLive));

        // The transcript is the only focusable control. When focus reaches the list
        // container itself, push it onto an item so the arrows work immediately.
        TranscriptList.GotKeyboardFocus += TranscriptList_GotKeyboardFocus;

        // Tab does nothing in this app: the menu bar is reached with Alt/F10 and the
        // transcript is the only content, so swallow Tab/Shift+Tab entirely. When the
        // transcript is empty there are no items to move to, so swallow the arrows too
        // rather than let focus wander off the list (e.g. onto the menu bar).
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                return;
            }

            // Panel mode has no focusable content, so the bare arrows are free: use them
            // to step through history the way they step through the transcript's list.
            // Skipped while the menu bar has focus, where the arrows navigate the menu.
            if (_panelMode && !MenuBar.IsKeyboardFocusWithin)
            {
                switch (e.Key)
                {
                    case Key.Up or Key.Left or Key.PageUp: StepPanel(-1); e.Handled = true; return;
                    case Key.Down or Key.Right or Key.PageDown: StepPanel(+1); e.Handled = true; return;
                    case Key.Home: ShowPanelLine(0, fromUser: true); e.Handled = true; return;
                    case Key.End: FollowLive(); e.Handled = true; return;
                }
            }

            if (_lines.Count == 0 && TranscriptList.IsKeyboardFocusWithin
                && e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
                e.Handled = true;
        };

        // Focus lands on the transcript when the window opens.
        Loaded += (_, _) => FocusTranscriptStart();

#if WINDOWS_AI
        // Offer the Windows on-device recognizer between Streaming and SAPI. When it
        // can't run here (unpackaged, unsupported OS), keep it visible but disabled
        // with the reason exposed to screen readers, so the capability is discoverable.
        _engWindowsAi = new MenuItem
        {
            Header = "Windows on-_device — NPU, same engine as Live Captions",
            IsCheckable = true
        };
        _engWindowsAi.Click += Engine_Click;
        if (EngSapi.Parent is MenuItem engineMenu)
            engineMenu.Items.Insert(engineMenu.Items.IndexOf(EngSapi), _engWindowsAi);
        var winAiUnavailable = UpdateWindowsAiMenuItem();
#endif

        // Reflect the default selections (system audio + Whisper) in menu enablement,
        // and seed the Application submenu (an empty MenuItem never opens as a submenu).
        UpdateAudioMenuState();
        RefreshApplicationMenu();

        // Test aids (like LIVECAPTIONS_SEED): LIVECAPTIONS_DIAG=<file> appends engine
        // states and captions to a log so a session can be verified without watching
        // the UI; LIVECAPTIONS_AUTOSTART=<whisper|streaming|windows-ai|sapi> selects
        // that engine and starts listening on launch. No effect when unset.
#if WINDOWS_AI
        Diag(winAiUnavailable is null
            ? "windows-ai engine: available"
            : $"windows-ai engine: unavailable — {winAiUnavailable}");
#endif
        if (Environment.GetEnvironmentVariable("LIVECAPTIONS_AUTOSTART") is string autostart)
        {
            Loaded += (_, _) =>
            {
                SelectEngine(autostart switch
                {
                    "streaming" => Engine.Streaming,
                    "windows-ai" => Engine.WindowsAi,
                    "sapi" => Engine.Sapi,
                    _ => Engine.Whisper
                });
                StartListening();
            };
        }

        Closed += (_, _) => _captions?.Dispose();
    }

    private bool AnnounceCaptions => MenuAnnounce.IsChecked;
    private bool ShowTimestamps => MenuShowTimestamps.IsChecked;

    // ---- Listening lifecycle --------------------------------------------------
    private void ToggleListen()
    {
        if (_captions is not null && _captions.State is CaptionState.Listening or CaptionState.Starting)
        {
            _captions.Stop();
            return;
        }
        StartListening();
    }

    private void StartListening()
    {
        // Build a fresh caption source for the current engine + source selection.
        _captions?.Dispose();
        _captions = CreateSelectedSource();
        _captions.PartialRecognized += OnPartial;
        _captions.FinalRecognized += OnFinal;
        _captions.StateChanged += OnStateChanged;
        Diag("start: " + _captions.EngineDescription);
        _captions.Start();
    }

    private enum Engine { Whisper, Streaming, WindowsAi, Sapi }

    private Engine SelectedEngine =>
        EngSapi.IsChecked ? Engine.Sapi
        : EngStreaming.IsChecked ? Engine.Streaming
#if WINDOWS_AI
        : _engWindowsAi?.IsChecked == true ? Engine.WindowsAi
#endif
        : Engine.Whisper;

    private ICaptionSource CreateSelectedSource()
    {
        if (SelectedEngine == Engine.Sapi)
            return new SystemSpeechCaptionSource(); // SAPI: microphone only

        // Every other engine consumes any capture source: the microphone, one
        // application, or the whole system mix. For the whole mix, a screen-reader
        // user's own reader is normally excluded (Audio ▸ Exclude screen reader
        // speech) so captions cover the meeting/video, not the reader's narration.
        IAudioCaptureSource capture =
            !SrcSystem.IsChecked ? new MicrophoneCaptureSource()
            : _selectedAppPid is int pid ? MakeAppCapture(pid, _selectedAppName)
            : MenuExcludeScreenReader.IsChecked
              && ScreenReaderDetector.FindRunning() is (int srPid, string srName)
                ? MakeExcludeCapture(srPid, srName)
                : new SystemAudioCaptureSource();

        return SelectedEngine switch
        {
            Engine.Streaming => new SherpaCaptionSource(capture),
#if WINDOWS_AI
            Engine.WindowsAi => new WindowsAiCaptionSource(capture),
#endif
            _ => new WhisperCaptionSource(capture, _selectedModel)
        };
    }

    // On some audio devices the process-loopback tap delivers only silence (both
    // include and exclude modes) while plain device loopback works — observed with a
    // hardware-offloaded "Hi-Res Audio" headphone output. The capture sources detect
    // that via ProcessLoopbackWatchdog; react so the user is never left with a silent,
    // hallucinating captioner and no explanation.

    private string? _excludeTapDeadNotice;

    private IAudioCaptureSource MakeExcludeCapture(int srPid, string srName)
    {
        var capture = new SystemAudioExceptProcessCaptureSource(srPid, srName);
        capture.TapSilent += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_captions is null || _captions.State is not (CaptionState.Listening or CaptionState.Starting))
                return;
            // Turn the toggle off (visible, persistent state); its Changed handler
            // announces this notice and restarts listening with plain loopback.
            _excludeTapDeadNotice =
                $"Sound is playing but none reached the captioner: this audio device does not " +
                $"support excluding {srName}'s speech. Exclusion is now off, and captions include " +
                $"all system audio — including your screen reader.";
            MenuExcludeScreenReader.IsChecked = false;
        });
        return capture;
    }

    private IAudioCaptureSource MakeAppCapture(int pid, string appName)
    {
        var capture = new ProcessAudioCaptureSource(pid, appName);
        capture.TapSilent += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_captions is null || _captions.State is not (CaptionState.Listening or CaptionState.Starting))
                return;
            SetStatus($"{appName} is playing sound, but none is reaching the captioner. " +
                      "This audio device may not support per-application capture; under " +
                      "Audio, Application, choose (All system audio) instead.");
        });
        return capture;
    }

    // Apply an audio-setting change immediately by restarting the running session.
    private void RestartIfListening()
    {
        if (_captions is not null && _captions.State is CaptionState.Listening or CaptionState.Starting)
            StartListening();
    }

    private void OnStateChanged(object? sender, CaptionStateChangedEventArgs e)
    {
        Diag($"state {e.State}: {e.Message}");
        Dispatcher.Invoke(() =>
        {
            var listening = e.State is CaptionState.Listening or CaptionState.Starting;
            MenuToggleListen.Header = listening ? "_Stop listening" : "_Start listening";

            // SetStatus both shows the message and announces it with interrupt:true,
            // so an error already interrupts the screen reader — no separate announce.
            if (!string.IsNullOrWhiteSpace(e.Message))
                SetStatus(e.Message!);
        });
    }

    // ---- Audio menu (source / engine / application) ---------------------------
    private void Source_Click(object sender, RoutedEventArgs e)
    {
        bool system = ReferenceEquals(sender, SrcSystem);
        SrcSystem.IsChecked = system;
        SrcMic.IsChecked = !system;

        // System audio needs an engine that accepts arbitrary audio (SAPI is mic-only).
        if (system && SelectedEngine == Engine.Sapi)
            SelectEngine(Engine.Whisper);

        UpdateAudioMenuState();
        SetStatus(system
            ? "System audio. Use Audio ▸ Application to capture one app, or leave it on all audio."
            : "Microphone selected.");
        RestartIfListening();
    }

    private void Engine_Click(object sender, RoutedEventArgs e)
    {
        SelectEngine(
            ReferenceEquals(sender, EngSapi) ? Engine.Sapi
            : ReferenceEquals(sender, EngStreaming) ? Engine.Streaming
#if WINDOWS_AI
            : ReferenceEquals(sender, _engWindowsAi) ? Engine.WindowsAi
#endif
            : Engine.Whisper);
        RestartIfListening();
    }

    private void SelectEngine(Engine engine)
    {
        EngWhisper.IsChecked = engine == Engine.Whisper;
        EngStreaming.IsChecked = engine == Engine.Streaming;
        EngSapi.IsChecked = engine == Engine.Sapi;
#if WINDOWS_AI
        if (_engWindowsAi is not null)
            _engWindowsAi.IsChecked = engine == Engine.WindowsAi;
#endif

        // SAPI can only caption the microphone.
        if (engine == Engine.Sapi && SrcSystem.IsChecked)
        {
            SrcSystem.IsChecked = false;
            SrcMic.IsChecked = true;
        }

        UpdateAudioMenuState();
        SetStatus(engine switch
        {
            Engine.Whisper => "Whisper engine: accurate, on-device. Microphone or system audio.",
            Engine.Streaming => "Streaming engine: word-by-word captions with the lowest delay, on-device. Microphone or system audio.",
            Engine.WindowsAi => "Windows on-device engine: the Live Captions recognizer, NPU-accelerated on Copilot+ PCs. Microphone or system audio.",
            _ => "Windows speech engine: instant, microphone only."
        });
    }

    // Availability of the Windows on-device engine can only be judged definitively at
    // certain moments (the first probe at startup can fail transiently), so re-check
    // whenever the Engine submenu opens rather than trusting one startup answer.
    private void MenuEngine_SubmenuOpened(object sender, RoutedEventArgs e)
    {
#if WINDOWS_AI
        UpdateWindowsAiMenuItem();
#endif
    }

#if WINDOWS_AI
    /// <summary>Returns the unavailability reason, or null when the engine is offered.</summary>
    private string? UpdateWindowsAiMenuItem()
    {
        if (_engWindowsAi is null)
            return null;

        var reason = WindowsAiCaptionSource.DescribeDefinitiveUnavailability();
        _engWindowsAi.IsEnabled = reason is null;
        _engWindowsAi.ToolTip = reason;
        System.Windows.Automation.AutomationProperties.SetHelpText(_engWindowsAi, reason ?? "");
        return reason;
    }
#endif

    private void UpdateAudioMenuState()
    {
        MenuApplication.IsEnabled = SelectedEngine != Engine.Sapi && SrcSystem.IsChecked;
        // Excluding the screen reader only makes sense for the whole system mix
        // (a single captured app never includes the reader's audio anyway).
        MenuExcludeScreenReader.IsEnabled = MenuApplication.IsEnabled && _selectedAppPid is null;
        MenuModel.IsEnabled = SelectedEngine == Engine.Whisper; // model only applies to Whisper
    }

    private void ExcludeScreenReader_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        // Turned off automatically because the tap was found dead — explain that.
        if (_excludeTapDeadNotice is string notice)
        {
            _excludeTapDeadNotice = null;
            SetStatus(notice);
            RestartIfListening();
            return;
        }

        SetStatus(MenuExcludeScreenReader.IsChecked
            ? ScreenReaderDetector.FindRunning() is (_, string name)
                ? $"System-audio captions will exclude {name}'s speech."
                : "System-audio captions will exclude your screen reader's speech (none detected right now)."
            : "System-audio captions will include everything, including your screen reader's speech.");
        RestartIfListening();
    }

    // ---- Whisper model (speed vs. accuracy) -----------------------------------
    private void Model_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } clicked || !Enum.TryParse(tag, out GgmlType model))
            return;

        _selectedModel = model;
        foreach (var obj in MenuModel.Items)
            if (obj is MenuItem mi)
                mi.IsChecked = ReferenceEquals(mi, clicked);

        // The header carries the human description ("Tiny — fastest, …"); echo it.
        SetStatus($"Whisper model: {clicked.Header.ToString()!.Replace("_", "")}.");
        RestartIfListening();
    }

    // ---- Bulk model management --------------------------------------------------
    private async void DownloadAllModels_Click(object sender, RoutedEventArgs e)
    {
        var missing = WhisperModelStore.AllModels.Where(m => !WhisperModelStore.IsDownloaded(m)).ToList();
        if (missing.Count == 0 && SherpaModelStore.IsDownloaded())
        {
            SetStatus("All models are already downloaded.");
            return;
        }

        MenuDownloadAll.IsEnabled = false;
        int failed = 0;
        try
        {
            for (int i = 0; i < missing.Count; i++)
            {
                var model = missing[i];
                var prefix = $"Model {i + 1} of {missing.Count}";
                try
                {
                    await WhisperModelStore.EnsureAsync(model,
                        msg => Dispatcher.Invoke(() => SetStatus($"{prefix}: {msg}")),
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    failed++;
                    SetStatus($"{prefix}: {WhisperModelStore.DisplayName(model)} failed — {ex.Message}");
                }
            }

            if (!SherpaModelStore.IsDownloaded())
            {
                try
                {
                    await SherpaModelStore.EnsureAsync(
                        msg => Dispatcher.Invoke(() => SetStatus($"Streaming model: {msg}")),
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    failed++;
                    SetStatus($"Streaming model failed — {ex.Message}");
                }
            }

            SetStatus(failed == 0
                ? "All models downloaded."
                : $"Model downloads finished with {failed} failure{(failed == 1 ? "" : "s")}.");
        }
        finally
        {
            MenuDownloadAll.IsEnabled = true;
        }
    }

    private void ClearModels_Click(object sender, RoutedEventArgs e)
    {
        // A loaded model file is locked, so end any active session first.
        if (_captions is not null)
        {
            _captions.Stop();
            _captions.Dispose();
            _captions = null;
        }

        var (whisperCount, whisperMb) = WhisperModelStore.ClearAll();
        var (sherpaCount, sherpaMb) = SherpaModelStore.ClearAll();
        int count = whisperCount + sherpaCount;
        double megabytes = whisperMb + sherpaMb;
        SetStatus(count == 0
            ? "No downloaded models to clear."
            : $"Cleared {count} model file{(count == 1 ? "" : "s")}, freeing {megabytes:0} MB.");
    }

    // Rebuild the Application submenu each time it opens so newly launched apps appear.
    // (It is also populated at startup: a MenuItem with no children is treated by WPF
    // as a plain item, so SubmenuOpened would never fire on an empty menu.)
    private void MenuApplication_SubmenuOpened(object sender, RoutedEventArgs e) =>
        RefreshApplicationMenu();

    private void RefreshApplicationMenu()
    {
        MenuApplication.Items.Clear();
        MenuApplication.Items.Add(MakeAppItem(null, "(All system audio)", ""));

        foreach (var (pid, label, name) in EnumerateAudioApps())
            MenuApplication.Items.Add(MakeAppItem(pid, label, name));
    }

    /// <summary>
    /// Apps offered for per-app capture. Primary source: audio sessions on the default
    /// output device — any process that is (or recently was) rendering audio, which
    /// catches packaged apps like Media Player whose windows are hosted elsewhere.
    /// Windowed processes are appended as a fallback for apps that haven't started
    /// playing yet.
    /// </summary>
    private static List<(int Pid, string Label, string Name)> EnumerateAudioApps()
    {
        var byPid = new Dictionary<int, (string Label, string Name, bool Playing)>();

        try
        {
            using var devEnum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var device = devEnum.GetDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];
                    int pid = (int)session.GetProcessID;
                    if (pid == 0 || pid == Environment.ProcessId)
                        continue; // system sounds / ourselves

                    using var proc = Process.GetProcessById(pid);
                    bool playing = session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive;
                    var label = playing ? $"{proc.ProcessName} — playing" : proc.ProcessName;
                    if (!byPid.TryGetValue(pid, out var existing) || (playing && !existing.Playing))
                        byPid[pid] = (label, proc.ProcessName, playing);
                }
                catch
                {
                    // Session ended or process gone; skip it.
                }
            }
        }
        catch
        {
            // No output device or session enumeration failed — fall back to windows only.
        }

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (byPid.ContainsKey(p.Id) || string.IsNullOrWhiteSpace(p.MainWindowTitle))
                    continue;
                var title = p.MainWindowTitle.Length > 40 ? p.MainWindowTitle[..40] + "…" : p.MainWindowTitle;
                byPid[p.Id] = ($"{p.ProcessName} — {title}", p.ProcessName, false);
            }
            catch
            {
                // Process exited or access denied; skip it.
            }
        }

        return byPid
            .OrderByDescending(kv => kv.Value.Playing) // currently-playing apps first
            .ThenBy(kv => kv.Value.Label, StringComparer.CurrentCultureIgnoreCase)
            .Select(kv => (kv.Key, kv.Value.Label, kv.Value.Name))
            .ToList();
    }

    private MenuItem MakeAppItem(int? pid, string header, string name)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = pid == _selectedAppPid,
            Tag = new AppTag(pid, name)
        };
        item.Click += App_Click;
        return item;
    }

    private void App_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AppTag tag })
            return;

        _selectedAppPid = tag.Pid;
        _selectedAppName = tag.Name;

        foreach (var obj in MenuApplication.Items)
            if (obj is MenuItem mi)
                mi.IsChecked = ReferenceEquals(mi, sender);

        UpdateAudioMenuState(); // the exclude toggle only applies to the whole mix
        SetStatus(tag.Pid is null
            ? "Capturing all system audio."
            : $"Capturing audio from {tag.Name}.");
        RestartIfListening();
    }

    private sealed record AppTag(int? Pid, string Name);

    // ---- Recognition results --------------------------------------------------
    private void OnPartial(object? sender, CaptionTextEventArgs e)
    {
        // Interim guesses update the "Now hearing" line only. We intentionally do NOT
        // announce every partial — that would flood a screen reader with half-words.
        Diag("partial: " + e.Text);
        Dispatcher.Invoke(() => InterimText.Text = e.Text);
    }

    private void OnFinal(object? sender, CaptionTextEventArgs e)
    {
        Diag("final: " + e.Text);
        Dispatcher.Invoke(() =>
        {
            InterimText.Text = "—";

            var line = new TranscriptLine(e.Text, DateTime.Now.ToString("HH:mm:ss"), ShowTimestamps);
            _lines.Add(line);

            // Keep the newest caption visible, but don't yank focus away from a user
            // who is currently reviewing earlier lines with the keyboard.
            if (!TranscriptList.IsKeyboardFocusWithin)
                TranscriptList.ScrollIntoView(line);

            // Panel mode's equivalent: advance to the new line only while following live,
            // so stepping back to re-read something isn't undone by the next caption.
            // fromUser:false — this line is already being announced below; announcing it
            // again from the panel would say it twice.
            if (_panelMode)
            {
                if (_followLive)
                    ShowPanelLine(_lines.Count - 1, fromUser: false);
                else
                    UpdatePanelChrome(); // the count changed even though the view didn't
            }

            if (AnnounceCaptions)
            {
                // interrupt:false so each finalized line is queued and spoken in order.
                ScreenReader.Announce(this, e.Text, interrupt: false, "captions");
            }
        });
    }

    // ---- Commands -------------------------------------------------------------
    private void ClearTranscript()
    {
        _lines.Clear();
        InterimText.Text = "—";
        _followLive = true;
        ShowPanelLine(-1, fromUser: false);
        SetStatus("Transcript cleared.");
    }

    private void CopyAll()
    {
        if (_lines.Count == 0)
            return;
        Clipboard.SetText(BuildTranscriptText());
        SetStatus($"Copied {_lines.Count} lines to the clipboard.");
    }

    private void SaveTranscript()
    {
        if (_lines.Count == 0)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Save transcript",
            Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"transcript-{DateTime.Now:yyyy-MM-dd-HHmm}.txt"
        };

        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, BuildTranscriptText(), Encoding.UTF8);
            SetStatus($"Saved transcript to {dialog.FileName}.");
        }
    }

    private string BuildTranscriptText()
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
            sb.AppendLine(ShowTimestamps ? $"[{line.Timestamp}] {line.Text}" : line.Text);
        return sb.ToString();
    }

    private void AdjustFont(double delta)
    {
        _captionFontSize = Math.Clamp(_captionFontSize + delta, MinFont, MaxFont);
        TranscriptList.FontSize = _captionFontSize;
        InterimText.FontSize = _captionFontSize;
        PanelCaption.FontSize = _captionFontSize + PanelFontBoost;
        SetStatus($"Caption text size: {_captionFontSize:0} point.");
    }

    // Land on the item the user was last on, or the first line if they haven't moved
    // within the list yet ("start of the list if focus was not moved by the user").
    private void FocusTranscriptStart()
    {
        if (_lines.Count == 0)
        {
            TranscriptList.Focus();
            return;
        }
        int index = TranscriptList.SelectedIndex >= 0 ? TranscriptList.SelectedIndex : 0;
        FocusListItem(index);
    }

    private void FocusListItem(int index)
    {
        var item = _lines[index];
        TranscriptList.ScrollIntoView(item);
        TranscriptList.UpdateLayout(); // realize the (virtualized) container
        if (TranscriptList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container)
            container.Focus();
        else
            TranscriptList.Focus();
    }

    private void TranscriptList_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.NewFocus, TranscriptList))
            Dispatcher.BeginInvoke(new Action(FocusTranscriptStart));
    }

    // ---- Presentation: transcript vs. panel ------------------------------------
    // Panel mode is what the built-in Live Captions does — one caption at a time, no
    // history on screen. It is a *rendering* choice only: _lines keeps accumulating, so
    // switching back to the transcript brings the whole session with it, and finalized
    // captions are still announced through the same notification path, so a screen-reader
    // user loses nothing by using it.

    // The panel shows a single line, so it can afford to be larger than the transcript's.
    private const double PanelFontBoost = 10;

    // Set while SetPresentation syncs the two menu checkmarks, so the resulting
    // Checked/Unchecked events don't re-enter.
    private bool _syncingPresentationMenu;

    private void Presentation_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _syncingPresentationMenu)
            return;

        // Radio semantics from whichever item changed: checking "Panel" and unchecking
        // "Transcript" both mean panel, and vice versa.
        var item = (MenuItem)sender;
        SetPresentation(ReferenceEquals(item, MenuPresentPanel) ? item.IsChecked : !item.IsChecked);
    }

    private void SetPresentation(bool panel)
    {
        _panelMode = panel;
        PanelView.Visibility = panel ? Visibility.Visible : Visibility.Collapsed;
        TranscriptView.Visibility = panel ? Visibility.Collapsed : Visibility.Visible;

        _syncingPresentationMenu = true;
        try
        {
            MenuPresentPanel.IsChecked = panel;
            MenuPresentTranscript.IsChecked = !panel;
        }
        finally { _syncingPresentationMenu = false; }

        if (panel)
        {
            // Entering the panel always starts live, at the newest caption.
            _followLive = true;
            ShowPanelLine(_lines.Count - 1, fromUser: false);

            // Focus was on a transcript item that is now collapsed. Park it on the window
            // so it is not stranded on a hidden control; nothing in the panel takes focus.
            Focus();
            SetStatus(_lines.Count == 0
                ? "Panel mode. One caption at a time. Press F7 for the full transcript."
                : $"Panel mode. All {_lines.Count} captions are kept — press F7 to review them.");
        }
        else
        {
            SetStatus($"Transcript mode. {_lines.Count} captions.");
            // After the visibility change, so the list has containers to focus.
            Dispatcher.BeginInvoke(new Action(FocusTranscriptStart));
        }

        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Show <paramref name="index"/> in the panel. <paramref name="fromUser"/> marks a
    /// deliberate move (arrows, buttons, menu) — those announce the line they landed on
    /// and decide whether to keep following live. Automatic advances pass false, because
    /// the arriving caption is already announced by <see cref="OnFinal"/>.
    /// </summary>
    private void ShowPanelLine(int index, bool fromUser)
    {
        if (_lines.Count == 0)
        {
            _panelIndex = -1;
            PanelCaption.Text = "—";
            UpdatePanelChrome();
            return;
        }

        index = Math.Clamp(index, 0, _lines.Count - 1);
        _panelIndex = index;
        var line = _lines[index];
        PanelCaption.Text = ShowTimestamps ? $"[{line.Timestamp}] {line.Text}" : line.Text;

        if (fromUser)
        {
            // Landing on the newest line means "caught up", so resume following.
            _followLive = index == _lines.Count - 1;
            // interrupt:true on its own activity id: holding an arrow key down supersedes
            // rather than queues, so stepping fast can't flood the screen reader, and it
            // never cancels the queued caption announcements.
            ScreenReader.Announce(this,
                _followLive ? $"{line.Text}. Following live." : $"{line.Text}. Caption {index + 1} of {_lines.Count}.",
                interrupt: true, "panel");
        }

        UpdatePanelChrome();
    }

    private void StepPanel(int delta)
    {
        if (!_panelMode || _lines.Count == 0)
            return;
        ShowPanelLine(_panelIndex < 0 ? _lines.Count - 1 : _panelIndex + delta, fromUser: true);
    }

    private void FollowLive()
    {
        if (!_panelMode)
            return;
        _followLive = true;
        ShowPanelLine(_lines.Count - 1, fromUser: true);
    }

    private void UpdatePanelChrome()
    {
        PanelPosition.Text = _lines.Count == 0
            ? "No captions yet"
            : _followLive
                ? $"Live — {_lines.Count} captions"
                : $"Caption {_panelIndex + 1} of {_lines.Count} — paused";

        // The buttons take their enabled state from the commands' CanExecute.
        CommandManager.InvalidateRequerySuggested();
    }

    // ---- View toggles ---------------------------------------------------------
    private void ShowTimestamps_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        foreach (var line in _lines)
            line.ShowTimestamp = ShowTimestamps;
        if (_panelMode)
            ShowPanelLine(_panelIndex, fromUser: false); // re-render with/without the stamp
        SetStatus(ShowTimestamps ? "Timestamps shown." : "Timestamps hidden.");
    }

    private void Announce_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        SetStatus(AnnounceCaptions
            ? "New captions will be announced by your screen reader."
            : _panelMode
                ? "Automatic announcements off. Step through captions with the arrow keys."
                : "Automatic announcements off. Review captions with the arrow keys in the transcript.");
    }

    // ---- Status ---------------------------------------------------------------
    private void SetStatus(string message)
    {
        StatusText.Text = message;
        ScreenReader.Announce(this, message, interrupt: true, "status");
    }

    // ---- Diagnostics (LIVECAPTIONS_DIAG) ---------------------------------------
    private static readonly string? DiagPath =
        Environment.GetEnvironmentVariable("LIVECAPTIONS_DIAG");

    private static void Diag(string line)
    {
        if (DiagPath is null)
            return;
        try { File.AppendAllText(DiagPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}"); }
        catch { /* diagnostics must never break the app */ }
    }
}
