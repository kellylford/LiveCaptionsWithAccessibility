using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace AccessibleLiveCaptions.Speech;

/// <summary>
/// Store for the streaming recognizer's model files (NVIDIA NeMo cache-aware streaming
/// FastConformer transducer, exported for sherpa-onnx). Unlike Whisper's single ggml
/// blob, a transducer is four files: encoder, decoder, joiner, and the token table.
/// Mirrors <see cref="WhisperModelStore"/>: same directory root, same
/// download-with-progress contract, cleared by the same menu command.
/// </summary>
public static class SherpaModelStore
{
    // The 480 ms-latency export: noticeably more accurate than the 80 ms variant
    // (verified: exact reference transcript vs. dropped word endings) and still far
    // snappier than a chunked recognizer; partials update roughly twice a second.
    private const string BaseUrl =
        "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-streaming-fast-conformer-transducer-en-480ms/resolve/main/";

    private static readonly string[] Files = ["encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt"];

    public const string DisplayName = "Streaming (English)";

    /// <summary>Total download size, for menu labels and progress text.</summary>
    public const string SizeText = "456 MB";

    public static string ModelDirectory => Path.Combine(
        WhisperModelStore.ModelsDirectory, "nemo-streaming-en-480ms");

    public static string EncoderPath => Path.Combine(ModelDirectory, "encoder.onnx");
    public static string DecoderPath => Path.Combine(ModelDirectory, "decoder.onnx");
    public static string JoinerPath => Path.Combine(ModelDirectory, "joiner.onnx");
    public static string TokensPath => Path.Combine(ModelDirectory, "tokens.txt");

    public static bool IsDownloaded() => Files.All(f =>
    {
        var info = new FileInfo(Path.Combine(ModelDirectory, f));
        return info.Exists && info.Length > 0;
    });

    /// <summary>
    /// Ensure all four model files exist locally, downloading any that are missing
    /// with throttled progress messages suitable for status-line announcement.
    /// </summary>
    public static async Task EnsureAsync(Action<string> progress, CancellationToken ct)
    {
        if (IsDownloaded())
            return;

        Directory.CreateDirectory(ModelDirectory);
        using var http = new HttpClient();

        for (int i = 0; i < Files.Length; i++)
        {
            var file = Files[i];
            var path = Path.Combine(ModelDirectory, file);
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 0)
                continue;

            var label = $"streaming model file {i + 1} of {Files.Length}";
            using var response = await http.GetAsync(BaseUrl + file,
                HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            progress($"Downloading {label} ({(total is long t ? $"{t / (1024.0 * 1024.0):0} MB" : "unknown size")})…");

            var tmp = path + ".part";
            long done = 0;
            int lastPercent = -10; // report every 10%
            var lastReport = Stopwatch.StartNew();

            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var fs = File.Create(tmp))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;

                    if (total is long len)
                    {
                        int percent = (int)(done * 100 / len);
                        if (percent >= lastPercent + 10)
                        {
                            lastPercent = percent;
                            progress($"Downloading {label}: {percent} percent.");
                        }
                    }
                    else if (lastReport.ElapsedMilliseconds >= 5000)
                    {
                        lastReport.Restart();
                        progress($"Downloading {label}: {done / (1024.0 * 1024.0):0} MB so far.");
                    }
                }
            }

            File.Move(tmp, path, overwrite: true);
        }

        progress("Streaming model downloaded.");
    }

    /// <summary>
    /// Delete the streaming model files (and any half-finished .part files).
    /// Returns (deleted count, megabytes freed). Files in use are skipped.
    /// </summary>
    public static (int Count, double Megabytes) ClearAll()
    {
        var dir = new DirectoryInfo(ModelDirectory);
        if (!dir.Exists)
            return (0, 0);

        int count = 0;
        long bytes = 0;
        foreach (var file in dir.EnumerateFiles())
        {
            try
            {
                var len = file.Length;
                file.Delete();
                count++;
                bytes += len;
            }
            catch
            {
                // In use (e.g. a loaded model) or access denied — leave it.
            }
        }
        return (count, bytes / (1024.0 * 1024.0));
    }
}
