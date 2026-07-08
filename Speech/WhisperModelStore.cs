using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Whisper.net.Ggml;

namespace AccessibleLiveCaptions.Speech;

/// <summary>
/// Shared store for the on-device Whisper model files: where they live, whether they
/// are present, downloading them with progress, and clearing them. Used both by
/// <see cref="WhisperCaptionSource"/> (fetch-on-first-use) and by the Audio menu's
/// "Download all models" / "Clear downloaded models" commands.
/// </summary>
public static class WhisperModelStore
{
    /// <summary>Every model offered in the Audio ▸ Whisper model menu.</summary>
    public static readonly GgmlType[] AllModels =
    [
        GgmlType.TinyEn, GgmlType.BaseEn, GgmlType.SmallEn,
        GgmlType.MediumEn, GgmlType.LargeV3Turbo, GgmlType.LargeV3
    ];

    public static string ModelsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AccessibleLiveCaptions", "models");

    public static string GetPath(GgmlType type) =>
        Path.Combine(ModelsDirectory, $"ggml-{type}.bin");

    public static bool IsDownloaded(GgmlType type)
    {
        var f = new FileInfo(GetPath(type));
        return f.Exists && f.Length > 0;
    }

    /// <summary>
    /// Ensure the model file exists locally, downloading with throttled progress
    /// messages (roughly every 10 percent) suitable for status-line announcement.
    /// Returns the local path.
    /// </summary>
    public static async Task<string> EnsureAsync(GgmlType type, Action<string> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ModelsDirectory);
        var path = GetPath(type);
        if (IsDownloaded(type))
            return path;

        var url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{ModelFileName(type)}.bin";

        using var http = new HttpClient();
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        var totalText = total is long t ? $"{t / (1024.0 * 1024.0):0} MB" : "unknown size";
        progress($"Downloading the {DisplayName(type)} model ({totalText})…");

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
                        progress($"Downloading the {DisplayName(type)} model: {percent} percent.");
                    }
                }
                else if (lastReport.ElapsedMilliseconds >= 5000)
                {
                    lastReport.Restart();
                    progress($"Downloading the {DisplayName(type)} model: {done / (1024.0 * 1024.0):0} MB so far.");
                }
            }
        }

        File.Move(tmp, path, overwrite: true);
        progress($"{DisplayName(type)} model downloaded.");
        return path;
    }

    /// <summary>
    /// Delete all downloaded model files (and any half-finished .part files).
    /// Returns (deleted count, megabytes freed). Files in use are skipped.
    /// </summary>
    public static (int Count, double Megabytes) ClearAll()
    {
        var dir = new DirectoryInfo(ModelsDirectory);
        if (!dir.Exists)
            return (0, 0);

        int count = 0;
        long bytes = 0;
        foreach (var file in dir.EnumerateFiles("*.bin").Concat(dir.EnumerateFiles("*.part")))
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

    public static string ModelFileName(GgmlType type) => type switch
    {
        GgmlType.TinyEn => "tiny.en",
        GgmlType.Tiny => "tiny",
        GgmlType.BaseEn => "base.en",
        GgmlType.Base => "base",
        GgmlType.SmallEn => "small.en",
        GgmlType.Small => "small",
        GgmlType.MediumEn => "medium.en",
        GgmlType.Medium => "medium",
        GgmlType.LargeV1 => "large-v1",
        GgmlType.LargeV2 => "large-v2",
        GgmlType.LargeV3 => "large-v3",
        GgmlType.LargeV3Turbo => "large-v3-turbo",
        _ => type.ToString().ToLowerInvariant()
    };

    public static string DisplayName(GgmlType type) => type switch
    {
        GgmlType.TinyEn or GgmlType.Tiny => "Tiny",
        GgmlType.BaseEn or GgmlType.Base => "Base",
        GgmlType.SmallEn or GgmlType.Small => "Small",
        GgmlType.MediumEn or GgmlType.Medium => "Medium",
        GgmlType.LargeV3Turbo => "Large v3 Turbo",
        GgmlType.LargeV1 or GgmlType.LargeV2 or GgmlType.LargeV3 => "Large",
        _ => type.ToString()
    };
}
