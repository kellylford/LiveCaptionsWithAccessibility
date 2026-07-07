using System.Collections.ObjectModel;
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

    public static readonly RoutedUICommand FocusTranscriptCommand =
        new("Go to transcript", nameof(FocusTranscriptCommand), typeof(MainWindow),
            [new KeyGesture(Key.T, ModifierKeys.Control)]);

    // F6 / Shift+F6 cycle focus between the panes (toolbars + transcript), the
    // standard Windows shortcut for moving between regions of a window.
    public static readonly RoutedUICommand CyclePaneCommand =
        new("Next pane", nameof(CyclePaneCommand), typeof(MainWindow),
            [new KeyGesture(Key.F6)]);

    public static readonly RoutedUICommand CyclePaneBackCommand =
        new("Previous pane", nameof(CyclePaneBackCommand), typeof(MainWindow),
            [new KeyGesture(Key.F6, ModifierKeys.Shift)]);

    // ---- State ----------------------------------------------------------------
    private readonly ObservableCollection<TranscriptLine> _lines = new();
    private ICaptionSource? _captions;
    private double _captionFontSize = 22;
    private const double MinFont = 14;
    private const double MaxFont = 48;

    public MainWindow()
    {
        InitializeComponent();

        TranscriptList.ItemsSource = _lines;

        CommandBindings.Add(new CommandBinding(ToggleListenCommand, (_, _) => ToggleListen()));
        CommandBindings.Add(new CommandBinding(ClearCommand, (_, _) => ClearTranscript()));
        CommandBindings.Add(new CommandBinding(CopyAllCommand, (_, _) => CopyAll(), (_, e) => e.CanExecute = _lines.Count > 0));
        CommandBindings.Add(new CommandBinding(SaveCommand, (_, _) => SaveTranscript(), (_, e) => e.CanExecute = _lines.Count > 0));
        CommandBindings.Add(new CommandBinding(IncreaseFontCommand, (_, _) => AdjustFont(+2)));
        CommandBindings.Add(new CommandBinding(DecreaseFontCommand, (_, _) => AdjustFont(-2)));
        CommandBindings.Add(new CommandBinding(ToggleAnnounceCommand, (_, _) => MenuAnnounce.IsChecked = !MenuAnnounce.IsChecked));
        CommandBindings.Add(new CommandBinding(FocusTranscriptCommand, (_, _) => FocusTranscript()));
        CommandBindings.Add(new CommandBinding(CyclePaneCommand, (_, _) => CyclePane(forward: true)));
        CommandBindings.Add(new CommandBinding(CyclePaneBackCommand, (_, _) => CyclePane(forward: false)));

        // When focus reaches the list container itself (via Tab or F6), push it onto an item.
        TranscriptList.GotKeyboardFocus += TranscriptList_GotKeyboardFocus;

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
        bool whisper = EngineCombo.SelectedIndex == 0;
        if (!whisper)
            return new SystemSpeechCaptionSource(); // SAPI: microphone only

        bool systemAudio = SourceCombo.SelectedIndex == 1;
        IAudioCaptureSource capture = systemAudio
            ? new SystemAudioCaptureSource()
            : new MicrophoneCaptureSource();
        return new WhisperCaptionSource(capture);
    }

    private void OnStateChanged(object? sender, CaptionStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var listening = e.State is CaptionState.Listening or CaptionState.Starting;
            ListenButton.Content = listening ? "_Stop listening" : "_Start listening";
            MenuToggleListen.Header = listening ? "_Stop listening" : "_Start listening";
            ListenButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,
                listening ? "Stop listening" : "Start listening");

            // Lock the engine/source pickers while a session is active.
            EngineCombo.IsEnabled = !listening;
            SourceCombo.IsEnabled = !listening && EngineCombo.SelectedIndex == 0;

            if (!string.IsNullOrWhiteSpace(e.Message))
                SetStatus(e.Message!);

            if (e.State == CaptionState.Error && !string.IsNullOrWhiteSpace(e.Message))
            {
                // Errors are important — make sure the screen reader interrupts with them.
                ScreenReader.Announce(this, e.Message!, interrupt: true, "status");
            }
        });
    }

    private void EngineCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        // Windows SAPI can only listen to the microphone; Whisper can also take system audio.
        bool whisper = EngineCombo.SelectedIndex == 0;
        if (!whisper)
            SourceCombo.SelectedIndex = 0;
        SourceCombo.IsEnabled = whisper;
        SetStatus(whisper
            ? "Whisper engine: accurate, on-device. Microphone or system audio."
            : "Windows speech engine: instant, microphone only.");
    }

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

    private void FocusTranscript() => FocusTranscriptStart();

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

    // F6 / Shift+F6: cycle focus across the toolbars and the transcript.
    private void CyclePane(bool forward)
    {
        FrameworkElement[] panes = [ActionToolBar, SettingsToolBar, TranscriptList];
        int current = Array.FindIndex(panes, p => p.IsKeyboardFocusWithin);
        int target = current < 0
            ? 0
            : (current + (forward ? 1 : panes.Length - 1)) % panes.Length;
        FocusPane(panes[target]);
    }

    private void FocusPane(FrameworkElement pane)
    {
        if (ReferenceEquals(pane, TranscriptList))
            FocusTranscriptStart();
        else
            pane.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
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
