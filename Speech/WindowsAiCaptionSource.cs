// Compiled only in the Windows AI flavor (dotnet build -p:WindowsAI=true); see the
// csproj. Requires the app to run MSIX-packaged with the systemAIModels capability.
#if WINDOWS_AI
using System.Runtime.InteropServices;
using AccessibleLiveCaptions.Audio;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Speech;

namespace AccessibleLiveCaptions.Speech;

/// <summary>
/// Caption engine backed by the Windows on-device speech recognizer — the same model
/// family that powers Windows Live Captions and voice typing. On Copilot+ PCs the
/// model is preinstalled and always runs on the NPU (near-zero CPU cost); on other
/// 24H2+ machines Windows downloads it on demand and runs it on the CPU.
///
/// Audio still comes from our own capture seam: normalized 16 kHz mono float frames
/// are converted to 16-bit PCM and pushed into a <see cref="SpeechAudioProvider"/>,
/// so the OS recognizer captions the microphone, one application, or the whole
/// system mix exactly like the other engines.
///
/// Constraints (as of Windows App SDK 2.2-experimental): the API is experimental,
/// English-only, and only available to MSIX-packaged apps that declare the
/// systemAIModels capability — <see cref="DescribeUnavailability"/> reports which
/// requirement is missing so the UI can explain instead of failing cryptically.
/// </summary>
public sealed class WindowsAiCaptionSource : ICaptionSource
{
    private readonly IAudioCaptureSource _capture;

    private SpeechAudioProvider? _provider;
    private StreamingRecognition? _recognition;
    private bool _disposed;

    public WindowsAiCaptionSource(IAudioCaptureSource capture)
    {
        _capture = capture;
        EngineDescription = $"Windows on-device recognizer (NPU on Copilot+) — {capture.Description}";
    }

    public event EventHandler<CaptionTextEventArgs>? PartialRecognized;
    public event EventHandler<CaptionTextEventArgs>? FinalRecognized;
    public event EventHandler<CaptionStateChangedEventArgs>? StateChanged;

    public CaptionState State { get; private set; } = CaptionState.Stopped;
    public string EngineDescription { get; }

    /// <summary>
    /// Null when the engine can run here; otherwise a user-facing sentence explaining
    /// what's missing (package identity, OS support, model state).
    /// </summary>
    public static string? DescribeUnavailability()
    {
        if (!HasPackageIdentity())
            return "The Windows recognizer needs the packaged (MSIX) version of this app.";

        try
        {
            return SpeechRecognitionModel.GetReadyState() switch
            {
                AIFeatureReadyState.Ready => null,
                AIFeatureReadyState.NotSupportedOnCurrentSystem =>
                    "This PC does not support the Windows on-device recognizer (requires Windows 11 24H2 or later).",
                AIFeatureReadyState.DisabledByUser =>
                    "The Windows on-device recognizer is disabled in Settings > System > AI components.",
                _ => null // NotReady/EnsureNeeded: Start() will download the model.
            };
        }
        catch (Exception ex)
        {
            return "The Windows on-device recognizer is not available: " + ex.Message;
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State is CaptionState.Listening or CaptionState.Starting)
            return;

        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            if (DescribeUnavailability() is string reason)
            {
                SetState(CaptionState.Error, reason);
                return;
            }

            if (SpeechRecognitionModel.GetReadyState() != AIFeatureReadyState.Ready)
            {
                // Preinstalled on Copilot+ PCs; on CPU-only machines Windows Update
                // fetches the model in the background on this call.
                SetState(CaptionState.Starting, "Preparing the Windows speech recognition model…");
                var ensure = await SpeechRecognitionModel.EnsureReadyAsync();
                if (ensure.Status != AIFeatureReadyResultState.Success)
                    throw new InvalidOperationException(
                        $"The speech recognition model is not available ({ensure.ExtendedError?.Message ?? ensure.Status.ToString()}).");
            }

            SetState(CaptionState.Starting, "Loading the Windows on-device recognizer…");
            var result = await SpeechRecognitionModel.TryCreateAsync();
            if (result.SpeechModel is not { } model)
                throw new InvalidOperationException(
                    $"The recognizer could not be created ({result.ExtendedError}).");

            _provider = new SpeechAudioProvider();
            _recognition = new StreamingRecognition(AudioConfiguration.ForProvider(_provider), model);
            _recognition.Recognizing += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Text))
                    PartialRecognized?.Invoke(this, new CaptionTextEventArgs(e.Text.Trim()));
            };
            _recognition.Recognized += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Text))
                    FinalRecognized?.Invoke(this, new CaptionTextEventArgs(e.Text.Trim()));
            };

            _capture.FrameAvailable += OnFrame;
            _capture.Failed += OnCaptureFailed;
            _capture.Start();

            await _recognition.StartContinuousRecognitionAsync();
            SetState(CaptionState.Listening, "Listening. Captions appear as speech is detected.");
        }
        catch (Exception ex)
        {
            SetState(CaptionState.Error, "Could not start the Windows recognizer: " + ex.Message);
        }
    }

    public void Stop()
    {
        if (State == CaptionState.Stopped)
            return;

        _capture.FrameAvailable -= OnFrame;
        _capture.Failed -= OnCaptureFailed;
        _capture.Stop();

        try { _recognition?.StopContinuousRecognition(); }
        catch { /* already stopped or never started */ }

        SetState(CaptionState.Stopped, "Stopped.");
    }

    // The provider expects 16 kHz, 16-bit, mono PCM — convert our float frames.
    private void OnFrame(object? sender, AudioFrameEventArgs e)
    {
        var provider = _provider;
        if (provider is null)
            return;

        var samples = e.Samples;
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            var s = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
            MemoryMarshal.Write(bytes.AsSpan(i * 2), in s);
        }

        try { provider.PushData(bytes); }
        catch { /* recognition tearing down */ }
    }

    private void OnCaptureFailed(object? sender, string message) =>
        SetState(CaptionState.Error, message);

    private static bool HasPackageIdentity()
    {
        int length = 0;
        // APPMODEL_ERROR_NO_PACKAGE (15700) when running unpackaged.
        return GetCurrentPackageFullName(ref length, null) != 15700;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    private void SetState(CaptionState state, string? message)
    {
        State = state;
        StateChanged?.Invoke(this, new CaptionStateChangedEventArgs(state, message));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Stop();
        _capture.Dispose();
        _recognition = null;
        _provider = null;
    }
}
#endif
