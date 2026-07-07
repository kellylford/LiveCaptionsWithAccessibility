using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace AccessibleLiveCaptions.Accessibility;

/// <summary>
/// Helper for speaking arbitrary text to a screen reader (Narrator, NVDA, JAWS)
/// without hijacking keyboard focus.
///
/// We use the UI Automation *Notification* event (UIA 3, supported by WPF on
/// .NET Core 3+). It is the modern, reliable way to make a screen reader announce
/// a message: unlike moving focus or a persistent live region, it does not disturb
/// where the user is reading, and the processing hint lets us control whether
/// messages queue up or the newest supersedes older ones.
/// </summary>
public static class ScreenReader
{
    /// <summary>
    /// Announce <paramref name="message"/> via the given element's automation peer.
    /// </summary>
    /// <param name="source">Any live UIElement (typically the window).</param>
    /// <param name="message">Text to speak.</param>
    /// <param name="interrupt">
    /// When true, newer messages replace still-pending ones (good for transient
    /// status like "Listening…"). When false, messages queue so each finalized
    /// caption line is spoken in order.
    /// </param>
    /// <param name="activityId">
    /// Stable id grouping a stream of related notifications, so the screen reader
    /// can coalesce them sensibly.
    /// </param>
    public static void Announce(UIElement source, string message, bool interrupt, string activityId)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var peer = UIElementAutomationPeer.FromElement(source)
                   ?? UIElementAutomationPeer.CreatePeerForElement(source);
        if (peer is null)
            return;

        var processing = interrupt
            ? AutomationNotificationProcessing.MostRecent
            : AutomationNotificationProcessing.All;

        peer.RaiseNotificationEvent(
            AutomationNotificationKind.Other,
            processing,
            message,
            activityId);
    }
}
