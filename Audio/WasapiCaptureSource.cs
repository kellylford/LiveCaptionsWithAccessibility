using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace AccessibleLiveCaptions.Audio;

/// <summary>
/// Base for WASAPI-backed capture. Takes whatever format the device provides
/// (typically 32-bit float, stereo, 44.1/48 kHz), downmixes to mono and resamples to
/// 16 kHz, then hands out normalized frames. Subclasses only choose the device:
///   * <see cref="MicrophoneCaptureSource"/> — the default recording device.
///   * <see cref="SystemAudioCaptureSource"/> — render loopback (everything you hear).
///
/// Loopback is the answer to "capture other audio on the system": it taps the audio
/// the OS is about to play, so a Teams call, a browser video, anything, becomes
/// captionable without special drivers like "Stereo Mix".
/// </summary>
public abstract class WasapiCaptureSource : IAudioCaptureSource
{
    private readonly IWaveIn _capture;
    private WdlResampler? _resampler;
    private int _channels;
    private int _bits;
    private WaveFormatEncoding _encoding;
    private int _sourceRate;
    private bool _running;

    protected WasapiCaptureSource(IWaveIn capture, string description)
    {
        _capture = capture;
        Description = description;
    }

    public event EventHandler<AudioFrameEventArgs>? FrameAvailable;
    public event EventHandler<string>? Failed;

    public int SampleRate => 16000;
    public string Description { get; }

    public void Start()
    {
        try
        {
            var wf = _capture.WaveFormat;
            _channels = wf.Channels;
            _bits = wf.BitsPerSample;
            _encoding = wf.Encoding;
            _sourceRate = wf.SampleRate;

            _resampler = new WdlResampler();
            _resampler.SetMode(interp: true, filtercnt: 2, sinc: false);
            _resampler.SetFilterParms();
            _resampler.SetFeedMode(true); // we push input as it arrives
            _resampler.SetRates(_sourceRate, SampleRate);

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _running = true;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, "Could not start audio capture: " + ex.Message);
        }
    }

    public void Stop()
    {
        if (!_running)
            return;
        _running = false;
        try { _capture.StopRecording(); } catch { /* tearing down */ }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_running || _resampler is null)
            return;

        var mono = DecodeToMono(e.Buffer, e.BytesRecorded);
        if (mono.Length == 0)
            return;

        // Push through the resampler (feed mode, mono).
        int inCount = mono.Length;
        int prepared = _resampler.ResamplePrepare(inCount, 1, out float[] inBuffer, out int inOffset);
        int toCopy = Math.Min(prepared, inCount);
        Array.Copy(mono, 0, inBuffer, inOffset, toCopy);

        int maxOut = (int)(inCount * (double)SampleRate / _sourceRate) + 16;
        var outBuffer = new float[maxOut];
        int outCount = _resampler.ResampleOut(outBuffer, 0, toCopy, maxOut, 1);
        if (outCount <= 0)
            return;

        if (outCount != outBuffer.Length)
            Array.Resize(ref outBuffer, outCount);

        FrameAvailable?.Invoke(this, new AudioFrameEventArgs(outBuffer));
    }

    private float[] DecodeToMono(byte[] buffer, int bytes)
    {
        int bytesPerSample = _bits / 8;
        if (bytesPerSample == 0)
            return [];

        int frameSize = bytesPerSample * _channels;
        int frames = bytes / frameSize;
        var mono = new float[frames];

        for (int f = 0; f < frames; f++)
        {
            float sum = 0f;
            int baseIdx = f * frameSize;
            for (int c = 0; c < _channels; c++)
            {
                int idx = baseIdx + c * bytesPerSample;
                sum += _encoding == WaveFormatEncoding.IeeeFloat
                    ? BitConverter.ToSingle(buffer, idx)
                    : _bits == 16 ? BitConverter.ToInt16(buffer, idx) / 32768f
                    : _bits == 32 ? BitConverter.ToInt32(buffer, idx) / 2147483648f
                    : 0f;
            }
            mono[f] = sum / _channels;
        }
        return mono;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null && _running)
            Failed?.Invoke(this, "Audio capture stopped unexpectedly: " + e.Exception.Message);
    }

    public void Dispose()
    {
        Stop();
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Captures the default microphone.</summary>
public sealed class MicrophoneCaptureSource() : WasapiCaptureSource(
    new WasapiCapture { ShareMode = AudioClientShareMode.Shared },
    "Microphone");

/// <summary>
/// Captures system render loopback — everything currently playing on the PC.
/// This is what lets the app caption meetings, videos, and other apps' audio.
/// </summary>
public sealed class SystemAudioCaptureSource() : WasapiCaptureSource(
    new WasapiLoopbackCapture(),
    "System audio (what you hear)");

/// <summary>
/// Captures the audio of a single application (and its children) via Windows
/// process loopback — e.g. only the browser or only Teams, not the whole system mix.
/// </summary>
public sealed class ProcessAudioCaptureSource(int processId, string appName) : WasapiCaptureSource(
    new ProcessLoopbackWaveIn(processId),
    $"App audio — {appName}");

/// <summary>
/// Captures the whole system mix EXCEPT one process tree, via process loopback's
/// exclude mode. Built for the app's core audience: a screen-reader user's PC always
/// has the screen reader speaking, so "caption what I hear" must be able to mean
/// "…except my screen reader's own voice" — otherwise the meeting captions are
/// interleaved with the screen reader's narration (including its announcements of the
/// captions themselves).
/// </summary>
public sealed class SystemAudioExceptProcessCaptureSource(int processId, string name) : WasapiCaptureSource(
    new ProcessLoopbackWaveIn(processId, includeProcessTree: false),
    $"System audio except {name}");
