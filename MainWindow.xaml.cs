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

    // ---- State ----------------------------------------------------------------
    private readonly ObservableCollection<TranscriptLine> _lines = new();
    private ICaptionSource? _captions;
    private double _captionFontSize = 22;
    private const double MinFont = 14;
    private const double MaxFont = 48;

    // Selected application for per-app system-audio capture (null = whole system mix).
    private int? _selectedAppPid;
    private string _selectedAppName = "";

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
                e.Handled = true;
            else if (_lines.Count == 0 && TranscriptList.IsKeyboardFocusWithin
                     && e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
                e.Handled = true;
        };

        // Focus lands on the transcript when the window opens.
        Loaded += (_, _) => FocusTranscriptStart();

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
        _captions.Start();
    }

    private ICaptionSource CreateSelectedSource()
    {
        if (!EngWhisper.IsChecked)
            return new SystemSpeechCaptionSource(); // SAPI: microphone only

        if (!SrcSystem.IsChecked)
            return new WhisperCaptionSource(new MicrophoneCaptureSource());

        // System audio: a specific application, or the whole mix.
        if (_selectedAppPid is int pid)
            return new WhisperCaptionSource(new ProcessAudioCaptureSource(pid, _selectedAppName));
        return new WhisperCaptionSource(new SystemAudioCaptureSource());
    }

    private void OnStateChanged(object? sender, CaptionStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var listening = e.State is CaptionState.Listening or CaptionState.Starting;
            MenuToggleListen.Header = listening ? "_Stop listening" : "_Start listening";

            // Lock the audio source/engine/application choices while a session is active.
            MenuAudio.IsEnabled = !listening;

            if (!string.IsNullOrWhiteSpace(e.Message))
                SetStatus(e.Message!);

            if (e.State == CaptionState.Error && !string.IsNullOrWhiteSpace(e.Message))
            {
                // Errors are important — make sure the screen reader interrupts with them.
                ScreenReader.Announce(this, e.Message!, interrupt: true, "status");
            }
        });
    }

    // ---- Audio menu (source / engine / application) ---------------------------
    private void Source_Click(object sender, RoutedEventArgs e)
    {
        bool system = ReferenceEquals(sender, SrcSystem);
        SrcSystem.IsChecked = system;
        SrcMic.IsChecked = !system;

        // System audio needs the Whisper engine (SAPI is microphone-only).
        if (system && !EngWhisper.IsChecked)
            SelectEngine(whisper: true);

        UpdateAudioMenuState();
        SetStatus(system
            ? "System audio. Use Audio ▸ Application to capture one app, or leave it on all audio."
            : "Microphone selected.");
    }

    private void Engine_Click(object sender, RoutedEventArgs e) =>
        SelectEngine(whisper: ReferenceEquals(sender, EngWhisper));

    private void SelectEngine(bool whisper)
    {
        EngWhisper.IsChecked = whisper;
        EngSapi.IsChecked = !whisper;

        // SAPI can only caption the microphone.
        if (!whisper && SrcSystem.IsChecked)
        {
            SrcSystem.IsChecked = false;
            SrcMic.IsChecked = true;
        }

        UpdateAudioMenuState();
        SetStatus(whisper
            ? "Whisper engine: accurate, on-device. Microphone or system audio."
            : "Windows speech engine: instant, microphone only.");
    }

    private void UpdateAudioMenuState() =>
        MenuApplication.IsEnabled = EngWhisper.IsChecked && SrcSystem.IsChecked;

    // Rebuild the Application submenu each time it opens so newly launched apps appear.
    private void MenuApplication_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        MenuApplication.Items.Clear();
        MenuApplication.Items.Add(MakeAppItem(null, "(All system audio)", ""));

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (string.IsNullOrWhiteSpace(p.MainWindowTitle))
                    continue;
                var title = p.MainWindowTitle.Length > 40 ? p.MainWindowTitle[..40] + "…" : p.MainWindowTitle;
                MenuApplication.Items.Add(MakeAppItem(p.Id, $"{p.ProcessName} — {title}", p.ProcessName));
            }
            catch
            {
                // Process exited or access denied; skip it.
            }
        }
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

        SetStatus(tag.Pid is null
            ? "Capturing all system audio."
            : $"Capturing audio from {tag.Name}.");
    }

    private sealed record AppTag(int? Pid, string Name);

    // ---- Recognition results --------------------------------------------------
    private void OnPartial(object? sender, CaptionTextEventArgs e)
    {
        // Interim guesses update the "Now hearing" line only. We intentionally do NOT
        // announce every partial — that would flood a screen reader with half-words.
        Dispatcher.Invoke(() => InterimText.Text = e.Text);
    }

    private void OnFinal(object? sender, CaptionTextEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            InterimText.Text = "—";

            var line = new TranscriptLine(e.Text, DateTime.Now.ToString("HH:mm:ss"), ShowTimestamps);
            _lines.Add(line);

            // Keep the newest caption visible, but don't yank focus away from a user
            // who is currently reviewing earlier lines with the keyboard.
            if (!TranscriptList.IsKeyboardFocusWithin)
                TranscriptList.ScrollIntoView(line);

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

    // ---- View toggles ---------------------------------------------------------
    private void ShowTimestamps_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        foreach (var line in _lines)
            line.ShowTimestamp = ShowTimestamps;
        SetStatus(ShowTimestamps ? "Timestamps shown." : "Timestamps hidden.");
    }

    private void Announce_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        SetStatus(AnnounceCaptions
            ? "New captions will be announced by your screen reader."
            : "Automatic announcements off. Review captions with the arrow keys in the transcript.");
    }

    // ---- Status ---------------------------------------------------------------
    private void SetStatus(string message)
    {
        StatusText.Text = message;
        ScreenReader.Announce(this, message, interrupt: true, "status");
    }
}
