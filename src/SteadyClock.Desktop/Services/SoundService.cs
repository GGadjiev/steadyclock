using System.Runtime.InteropServices;
using Avalonia.Platform;
using MyTimeTracker.Core.Abstractions;
using NAudio.Wave;

namespace SteadyClock.Desktop.Services;

public sealed record CustomSoundImportResult(
    bool Success,
    string? Error,
    string DisplayName)
{
    public static CustomSoundImportResult Failed(string error) =>
        new(false, error, string.Empty);

    public static CustomSoundImportResult Imported(string displayName) =>
        new(true, null, displayName);
}

/// <summary>
/// Plays bundled WAV presets through winmm and stores a user-provided
/// notification sound as a 16-bit PCM WAV. Missing audio support is non-fatal.
/// </summary>
public sealed class SoundService : IDisposable
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndFilename = 0x00020000;
    private static readonly TimeSpan MaxCustomSoundDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromMilliseconds(100);
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".m4a", ".wma", ".aac"
    };

    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private readonly string _cacheDirectory;
    private readonly string _customSoundPath;
    private bool _disposed;

    public SoundService(IAppLogger logger, AppPaths paths)
    {
        _logger = logger;
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "SteadyClock", "sounds");
        _customSoundPath = Path.Combine(paths.SoundsDirectory, "custom.wav");
    }

    public string CustomSoundPath => _customSoundPath;
    public bool HasCustomSoundFile => File.Exists(_customSoundPath);

    public void Play(string? soundId)
    {
        if (_disposed || string.IsNullOrWhiteSpace(soundId) || soundId == SoundPresetCatalog.None.Id)
        {
            return;
        }

        try
        {
            string? path;
            if (soundId == SoundPresetCatalog.Custom.Id)
            {
                path = HasCustomSoundFile ? _customSoundPath : null;
            }
            else
            {
                var preset = SoundPresetCatalog.Get(soundId);
                path = string.IsNullOrWhiteSpace(preset.FileName)
                    ? null
                    : EnsureCached(preset);
            }

            if (path is null)
            {
                return;
            }

            lock (_sync)
            {
                PlaySound(null, IntPtr.Zero, 0);
                PlaySound(path, IntPtr.Zero, SndFilename | SndAsync | SndNoDefault);
            }
        }
        catch (Exception exception)
        {
            _logger.Warn($"Не удалось воспроизвести звук: {exception.Message}");
        }
    }

    public Task<CustomSoundImportResult> ImportCustomSoundAsync(
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ImportCustomSoundCore(sourcePath, cancellationToken), cancellationToken);

    public void Stop()
    {
        lock (_sync)
        {
            PlaySound(null, IntPtr.Zero, 0);
        }
    }

    private CustomSoundImportResult ImportCustomSoundCore(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return CustomSoundImportResult.Failed("Файл не найден.");
        }

        if (!SupportedExtensions.Contains(Path.GetExtension(sourcePath)))
        {
            return CustomSoundImportResult.Failed(
                "Поддерживаются WAV, MP3, M4A, WMA и AAC.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_customSoundPath)!);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(_customSoundPath)!,
            $"custom-{Guid.NewGuid():N}.tmp.wav");

        try
        {
            using (var reader = new MediaFoundationReader(sourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.TotalTime > MaxCustomSoundDuration + DurationTolerance)
                {
                    return CustomSoundImportResult.Failed(
                        "Звук длиннее 5 секунд. Выберите файл короче.");
                }

                WaveFileWriter.CreateWaveFile16(
                    temporaryPath,
                    reader.ToSampleProvider());
            }

            using (var check = new WaveFileReader(temporaryPath))
            {
                if (check.TotalTime > MaxCustomSoundDuration + DurationTolerance)
                {
                    return CustomSoundImportResult.Failed(
                        "Звук длиннее 5 секунд. Выберите файл короче.");
                }

                if (check.TotalTime <= TimeSpan.Zero)
                {
                    return CustomSoundImportResult.Failed(
                        "Не удалось прочитать аудиофайл.");
                }
            }

            Stop();
            File.Move(temporaryPath, _customSoundPath, overwrite: true);
            return CustomSoundImportResult.Imported(Path.GetFileName(sourcePath));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Warn($"Не удалось загрузить свой звук: {exception.Message}");
            return CustomSoundImportResult.Failed(
                "Не удалось прочитать аудиофайл. Попробуйте WAV или MP3.");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // The temporary file is harmless and will be overwritten next time.
                }
            }
        }
    }

    private string? EnsureCached(SoundPreset preset)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var path = Path.Combine(_cacheDirectory, preset.FileName);
        if (File.Exists(path))
        {
            return path;
        }

        using var source = AssetLoader.Open(
            new Uri($"avares://SteadyClock/Assets/Sounds/{preset.FileName}"));
        using var destination = File.Create(path);
        source.CopyTo(destination);
        return path;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
