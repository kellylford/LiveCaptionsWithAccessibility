using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;

namespace AccessibleLiveCaptions.Audio;

/// <summary>
/// Captures the audio of a single application (and its child processes) using the
/// Windows process-loopback API — <c>ActivateAudioInterfaceAsync</c> with
/// <c>AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS</c> (Windows 10 20H1+/Windows 11). This is
/// what lets the app caption "just the browser" or "just Teams" instead of the whole
/// system mix.
///
/// It presents as an NAudio <see cref="IWaveIn"/> so it drops straight into the existing
/// <see cref="WasapiCaptureSource"/> pipeline (mono downmix + resample to 16 kHz).
///
/// Everything runs on a dedicated MTA thread: the async activation callback is delivered
/// on an MTA thread, and creating our completion handler there avoids COM marshalling
/// back to the (STA) UI thread, which would deadlock while we wait for activation.
/// </summary>
public sealed class ProcessLoopbackWaveIn : IWaveIn
{
    private const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";
    private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    private const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    private const int AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK = 1;

    private readonly uint _targetPid;
    private readonly bool _includeTree;

    private Thread? _thread;
    private volatile bool _stop;

    public ProcessLoopbackWaveIn(int processId, bool includeProcessTree = true)
    {
        _targetPid = (uint)processId;
        _includeTree = includeProcessTree;
        // Fixed capture format; the loopback engine converts the app's audio to this.
        WaveFormat = new WaveFormat(48000, 16, 2);
    }

    public WaveFormat WaveFormat { get; set; }
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public void StartRecording()
    {
        _stop = false;
        _thread = new Thread(CaptureThread) { IsBackground = true, Name = "ProcessLoopbackCapture" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void StopRecording()
    {
        _stop = true;
        if (_thread is { } t && t.IsAlive && Thread.CurrentThread != t)
            t.Join(TimeSpan.FromSeconds(2));
    }

    private void CaptureThread()
    {
        Exception? error = null;
        IntPtr formatPtr = IntPtr.Zero;
        IntPtr paramsPtr = IntPtr.Zero;
        IntPtr propVariantPtr = IntPtr.Zero;
        try
        {
            // --- Build AUDIOCLIENT_ACTIVATION_PARAMS wrapped in a VT_BLOB PROPVARIANT ---
            var activation = new AudioClientActivationParams
            {
                ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
                TargetProcessId = _targetPid,
                ProcessLoopbackMode = _includeTree ? 0 : 1 // 0 = include target + tree, 1 = exclude
            };
            paramsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AudioClientActivationParams>());
            Marshal.StructureToPtr(activation, paramsPtr, false);

            var pv = new PropVariantBlob { vt = 0x41 /* VT_BLOB */, cbSize = Marshal.SizeOf<AudioClientActivationParams>(), pBlobData = paramsPtr };
            propVariantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantBlob>());
            Marshal.StructureToPtr(pv, propVariantPtr, false);

            // --- Activate the process-loopback audio client (async, awaited via event) ---
            var handler = new ActivationHandler();
            ActivateAudioInterfaceAsync(VirtualAudioDeviceProcessLoopback, IID_IAudioClient, propVariantPtr, handler, out _);
            if (!handler.Completed.WaitOne(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timed out activating process-loopback audio interface.");

            int hr = handler.Operation!.GetActivateResult(out int activateResult, out object clientObj);
            Marshal.ThrowExceptionForHR(hr);
            Marshal.ThrowExceptionForHR(activateResult);
            var audioClient = (IAudioClient)clientObj;

            // --- Initialize in loopback + auto-convert mode to our fixed capture format ---
            formatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEFORMATEX>());
            Marshal.StructureToPtr(WaveFormatExFor(WaveFormat), formatPtr, false);

            uint flags = AUDCLNT_STREAMFLAGS_LOOPBACK
                       | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
                       | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
            Marshal.ThrowExceptionForHR(
                audioClient.Initialize(AUDCLNT_SHAREMODE_SHARED, flags, 10_000_000, 0, formatPtr, IntPtr.Zero));

            Marshal.ThrowExceptionForHR(audioClient.GetService(IID_IAudioCaptureClient, out object captureObj));
            var capture = (IAudioCaptureClient)captureObj;

            int blockAlign = WaveFormat.BlockAlign;
            Marshal.ThrowExceptionForHR(audioClient.Start());
            try
            {
                PumpLoop(capture, blockAlign);
            }
            finally
            {
                audioClient.Stop();
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            if (formatPtr != IntPtr.Zero) Marshal.FreeHGlobal(formatPtr);
            if (propVariantPtr != IntPtr.Zero) Marshal.FreeHGlobal(propVariantPtr);
            if (paramsPtr != IntPtr.Zero) Marshal.FreeHGlobal(paramsPtr);
            RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
        }
    }

    private void PumpLoop(IAudioCaptureClient capture, int blockAlign)
    {
        var buffer = new byte[blockAlign * 4800]; // ~100 ms headroom
        while (!_stop)
        {
            Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out uint packetFrames));
            if (packetFrames == 0)
            {
                Thread.Sleep(10);
                continue;
            }

            while (packetFrames > 0)
            {
                Marshal.ThrowExceptionForHR(
                    capture.GetBuffer(out IntPtr dataPtr, out uint frames, out uint bufferFlags, out _, out _));

                int bytes = (int)frames * blockAlign;
                if (bytes > buffer.Length)
                    buffer = new byte[bytes];

                if ((bufferFlags & AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                    Array.Clear(buffer, 0, bytes);           // silent packet: emit zeros
                else
                    Marshal.Copy(dataPtr, buffer, 0, bytes);

                capture.ReleaseBuffer(frames);

                if (bytes > 0)
                    DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));

                Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out packetFrames));
            }
        }
    }

    public void Dispose() => StopRecording();

    private static WAVEFORMATEX WaveFormatExFor(WaveFormat wf) => new()
    {
        wFormatTag = 1, // WAVE_FORMAT_PCM
        nChannels = (ushort)wf.Channels,
        nSamplesPerSec = (uint)wf.SampleRate,
        wBitsPerSample = (ushort)wf.BitsPerSample,
        nBlockAlign = (ushort)wf.BlockAlign,
        nAvgBytesPerSec = (uint)wf.AverageBytesPerSecond,
        cbSize = 0
    };

    // ----- Interop declarations -----------------------------------------------

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public int ActivationType;
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    // 64-bit PROPVARIANT: 2-byte vt + 6-byte reserved, then blob {ULONG cbSize; void* pBlobData}
    // with the pointer 8-byte aligned at offset 16.
    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariantBlob
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public int cbSize;
        [FieldOffset(16)] public IntPtr pBlobData;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig] int GetActivateResult(out int activateResult,
            [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler
    {
        public readonly ManualResetEvent Completed = new(false);
        public IActivateAudioInterfaceAsyncOperation? Operation;

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            Operation = activateOperation;
            Completed.Set();
            return 0; // S_OK
        }
    }

    // IAudioClient — full vtable order matters; unused methods are declared so slots align.
    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration,
            long periodicity, IntPtr format, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint bufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService([MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint numFrames, out uint flags,
            out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint numFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint numFramesInNextPacket);
    }
}
