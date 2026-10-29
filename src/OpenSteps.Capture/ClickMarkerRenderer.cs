using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace OpenSteps.Capture;

public static class ClickMarkerRenderer
{
    private const int Radius = 22;

    public static void Draw(Graphics graphics, int x, int y)
    {
        using var pen = new Pen(Color.FromArgb(230, 220, 30, 30), 5);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawEllipse(pen, x - Radius, y - Radius, Radius * 2, Radius * 2);
    }

    /// <summary>Writes a copy of <paramref name="sourcePath"/> with the click circle drawn at (x, y).</summary>
    public static void Render(string sourcePath, string outputPath, int x, int y)
    {
        using var source = new Bitmap(sourcePath);
        using var marked = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(marked))
        {
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            Draw(graphics, x, y);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        marked.Save(outputPath, ImageFormat.Png);
    }
}
