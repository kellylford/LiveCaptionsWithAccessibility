using System.ComponentModel;
using System.Windows;

namespace AccessibleLiveCaptions;

/// <summary>
/// One finalized caption in the transcript history. Each of these becomes a single,
/// individually-focusable item in the transcript ListBox, so a screen-reader user can
/// arrow through the conversation line by line.
/// </summary>
public sealed class TranscriptLine : INotifyPropertyChanged
{
    private bool _showTimestamp;

    public TranscriptLine(string text, string timestamp, bool showTimestamp)
    {
        Text = text;
        Timestamp = timestamp;
        _showTimestamp = showTimestamp;
    }

    public string Text { get; }

    public string Timestamp { get; }

    public Visibility TimestampVisibility =>
        _showTimestamp ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowTimestamp
    {
        get => _showTimestamp;
        set
        {
            if (_showTimestamp == value)
                return;
            _showTimestamp = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimestampVisibility)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Fallback accessible name / clipboard text if anything reads the item directly.
    public override string ToString() => Text;
}
