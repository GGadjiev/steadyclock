using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.App.Services;

internal static class TrayIconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Dictionary<TimerStatus, Icon> CreateIcons()
    {
        return new Dictionary<TimerStatus, Icon>
        {
            [TimerStatus.Running] = CreateIcon(Color.FromArgb(255, 33, 128, 96), filled: true),
            [TimerStatus.PausedManual] = CreateIcon(Color.FromArgb(255, 180, 116, 25), filled: true),
            [TimerStatus.PausedIdle] = CreateIcon(Color.FromArgb(255, 180, 116, 25), filled: true),
            [TimerStatus.Stopped] = CreateIcon(Color.FromArgb(255, 108, 121, 117), filled: false)
        };
    }

    private static Icon CreateIcon(Color color, bool filled)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        if (filled)
        {
            using var background = new SolidBrush(color);
            graphics.FillEllipse(background, 2, 2, 28, 28);
            using var face = new SolidBrush(Color.FromArgb(255, 248, 252, 250));
            graphics.FillEllipse(face, 8, 8, 16, 16);
        }
        else
        {
            using var ring = new Pen(color, 2.4f);
            graphics.DrawEllipse(ring, 3, 3, 26, 26);
            using var face = new SolidBrush(Color.FromArgb(245, 248, 252, 250));
            graphics.FillEllipse(face, 7, 7, 18, 18);
        }

        using var hands = new Pen(filled ? Color.FromArgb(255, 38, 94, 84) : color, 2.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(hands, 16, 16, 16, 10.5f);
        graphics.DrawLine(hands, 16, 16, 20.5f, 18.5f);
        using var center = new SolidBrush(filled ? Color.FromArgb(255, 38, 94, 84) : color);
        graphics.FillEllipse(center, 14.7f, 14.7f, 2.6f, 2.6f);

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
