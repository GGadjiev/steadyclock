using System.Runtime.InteropServices;
using Avalonia.Platform;
using MyTimeTracker.Core.Abstractions;

namespace SteadyClock.Desktop.Services;

/// <summary>
/// Plays bundled WAV presets through winmm. Missing audio support is non-fatal.
/// </summary>
public sealed class SoundService : IDisposable
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndFilename = 0x00020000;

    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private readonly string _cacheDirectory;
    private bool _disposed;

    public SoundService(IAppLogger logger)
    {
        _logger = logger;
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "SteadyClock", "sounds");
    }

    public void Play(string? soundId)
    {
        if (_disposed || string.IsNullOrWhiteSpace(soundId) || soundId == SoundPresetCatalog.None.Id)
        {
            return;
        }

        var preset = SoundPresetCatalog.Get(soundId);
        if (string.IsNullOrWhiteSpace(preset.FileName))
        {
            return;
        }

        try
        {
            var path = EnsureCached(preset);
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

    public void Stop()
    {
        lock (_sync)
        {
            PlaySound(null, IntPtr.Zero, 0);
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
