param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\SteadyClock.Desktop\Assets\Sounds')
)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

public static class SteadyClockSoundGenerator
{
    private const int SampleRate = 44100;

    private static void WriteWave(string path, double durationSeconds, Func<double, double> sample)
    {
        int sampleCount = (int)(SampleRate * durationSeconds);
        int dataSize = sampleCount * 2;

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream, Encoding.ASCII))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);      // PCM
            writer.Write((short)1);      // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2); // byte rate
            writer.Write((short)2);      // block align
            writer.Write((short)16);     // bits per sample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            for (int i = 0; i < sampleCount; i++)
            {
                double t = (double)i / SampleRate;
                double fadeIn = Math.Min(1.0, t / 0.008);
                double fadeOut = Math.Min(1.0, (durationSeconds - t) / 0.03);
                double envelope = Math.Max(0.0, Math.Min(1.0, Math.Min(fadeIn, fadeOut)));
                double value = Math.Max(-1.0, Math.Min(1.0, sample(t) * envelope));
                writer.Write((short)(value * short.MaxValue * 0.55));
            }
        }
    }

    private static double Tone(double t, double start, double end, double frequency)
    {
        if (t < start || t >= end)
        {
            return 0;
        }

        double local = t - start;
        double noteLength = end - start;
        double envelope = Math.Min(1.0, local / 0.012) *
                          Math.Min(1.0, (noteLength - local) / 0.035);
        return Math.Sin(2 * Math.PI * frequency * local) * Math.Max(0, envelope);
    }

    public static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);

        WriteWave(Path.Combine(directory, "soft.wav"), 0.34, t =>
            Tone(t, 0.00, 0.16, 659.25) * 0.65 +
            Tone(t, 0.14, 0.34, 880.00) * 0.65);

        WriteWave(Path.Combine(directory, "bell.wav"), 0.80, t =>
        {
            double decay = Math.Exp(-3.4 * t);
            return (Math.Sin(2 * Math.PI * 1046.5 * t) * 0.65 +
                    Math.Sin(2 * Math.PI * 1568.0 * t) * 0.28) * decay;
        });

        WriteWave(Path.Combine(directory, "twotone.wav"), 0.56, t =>
            Tone(t, 0.00, 0.20, 523.25) * 0.65 +
            Tone(t, 0.24, 0.56, 659.25) * 0.65);

        WriteWave(Path.Combine(directory, "click.wav"), 0.075, t =>
            Math.Sin(2 * Math.PI * 1200 * t) * Math.Exp(-32 * t) * 0.55);

        WriteWave(Path.Combine(directory, "warning.wav"), 0.50, t =>
            Tone(t, 0.00, 0.11, 440.0) * 0.55 +
            Tone(t, 0.15, 0.26, 330.0) * 0.55 +
            Tone(t, 0.30, 0.50, 440.0) * 0.55);
    }
}
'@

[SteadyClockSoundGenerator]::Generate((New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName)
Get-ChildItem $OutputDirectory -File | Select-Object Name, Length
