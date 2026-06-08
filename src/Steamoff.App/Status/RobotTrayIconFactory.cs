using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Media.Imaging;

namespace Steamoff.App.Status;

public static class RobotTrayIconFactory
{
    public static Icon CreateIcon(RobotStatusKind status, int size = 32)
    {
        using var bitmap = CreateBitmap(status, size);
        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    public static BitmapSource CreateBitmapSource(RobotStatusKind status, int size = 256)
    {
        using var bitmap = CreateBitmap(status, size);
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(size, size));
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    public static Bitmap CreateBitmap(RobotStatusKind status, int size)
    {
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var s = size / 128f;
        RectangleF R(float x, float y, float w, float h) => new(x * s, y * s, w * s, h * s);
        PointF P(float x, float y) => new(x * s, y * s);

        using var bgPath = RoundRect(R(1, 1, 126, 126), 28 * s);
        using var bg = new LinearGradientBrush(R(0, 0, 128, 128), Color.FromArgb(255, 7, 12, 42), Color.FromArgb(255, 17, 8, 38), 45);
        g.FillPath(bg, bgPath);

        using var blueGlow = new PathGradientBrush(bgPath)
        {
            CenterColor = Color.FromArgb(170, 0, 82, 225),
            SurroundColors = new[] { Color.FromArgb(0, 0, 82, 225) },
            FocusScales = new PointF(0.56f, 0.56f)
        };
        g.FillPath(blueGlow, bgPath);

        DrawPixelSparkles(g, s);

        using var backShell = new LinearGradientBrush(R(8, 12, 112, 108), Color.FromArgb(255, 255, 178, 32), Color.FromArgb(255, 175, 54, 8), 90);
        using var backShellPath = RoundRect(R(8, 14, 112, 98), 28 * s);
        g.FillPath(backShell, backShellPath);

        using var shellShadow = new SolidBrush(Color.FromArgb(160, 95, 24, 6));
        using var shellPathShadow = RoundRect(R(15, 23, 98, 88), 22 * s);
        g.FillPath(shellShadow, shellPathShadow);

        using var shell = new LinearGradientBrush(R(13, 12, 104, 98), Color.FromArgb(255, 255, 178, 34), Color.FromArgb(255, 255, 96, 0), 90);
        using var shellPath = RoundRect(R(12, 13, 104, 94), 24 * s);
        g.FillPath(shell, shellPath);

        using var shine = new SolidBrush(Color.FromArgb(160, 255, 239, 142));
        using var shinePath = RoundRect(R(32, 17, 68, 9), 4 * s);
        g.FillPath(shine, shinePath);
        using var sideDark = new SolidBrush(Color.FromArgb(110, 88, 24, 7));
        FillRound(g, sideDark, R(14, 50, 9, 33), 5 * s);
        FillRound(g, sideDark, R(105, 50, 9, 33), 5 * s);

        using var visor = new LinearGradientBrush(R(28, 42, 72, 48), Color.FromArgb(255, 8, 13, 18), Color.FromArgb(255, 28, 31, 37), 90);
        using var visorPath = RoundRect(R(27, 40, 74, 50), 11 * s);
        g.FillPath(visor, visorPath);

        using var visorGlow = new SolidBrush(Color.FromArgb(115, 255, 164, 18));
        using var visorGlowPath = RoundRect(R(31, 47, 66, 34), 8 * s);
        g.FillPath(visorGlow, visorGlowPath);

        using var light = new SolidBrush(Color.FromArgb(255, 255, 241, 170));
        switch (status)
        {
            case RobotStatusKind.Online:
                DrawOnlineFace(g, light, s);
                break;
            case RobotStatusKind.Offline:
                DrawOfflineFace(g, light, s);
                break;
            default:
                DrawWaitingFace(g, light, s);
                break;
        }

        using var baseBrush = new LinearGradientBrush(R(44, 103, 40, 10), Color.FromArgb(255, 68, 70, 78), Color.FromArgb(255, 10, 12, 18), LinearGradientMode.Horizontal);
        FillRound(g, baseBrush, R(45, 102, 38, 10), 4 * s);
        using var outline = new Pen(Color.FromArgb(130, 255, 160, 20), Math.Max(1.2f, 1.8f * s));
        g.DrawPath(outline, shellPath);

        return bitmap;

        void FillRound(Graphics graphics, Brush brush, RectangleF rect, float radius)
        {
            using var path = RoundRect(rect, radius);
            graphics.FillPath(brush, path);
        }

        void DrawOnlineFace(Graphics graphics, Brush brush, float scale)
        {
            graphics.FillEllipse(brush, R(39, 57, 18, 18));
            using var wink = new GraphicsPath();
            wink.AddBezier(P(72, 61), P(77, 54), P(86, 54), P(91, 62));
            wink.AddLine(P(91, 62), P(85, 67));
            wink.AddBezier(P(85, 67), P(80, 63), P(76, 63), P(72, 67));
            wink.CloseFigure();
            graphics.FillPath(brush, wink);

            using var smile = new GraphicsPath();
            smile.AddBezier(P(40, 76), P(52, 90), P(77, 91), P(92, 75));
            smile.AddLine(P(92, 82), P(87, 86));
            smile.AddBezier(P(87, 86), P(73, 96), P(51, 95), P(36, 82));
            smile.CloseFigure();
            graphics.FillPath(brush, smile);
        }

        void DrawOfflineFace(Graphics graphics, Brush brush, float scale)
        {
            FillRound(graphics, brush, R(37, 61, 22, 7), 3.5f * scale);
            FillRound(graphics, brush, R(70, 61, 22, 7), 3.5f * scale);
            FillRound(graphics, brush, R(60, 77, 12, 6), 3 * scale);
        }

        void DrawWaitingFace(Graphics graphics, Brush brush, float scale)
        {
            graphics.FillEllipse(brush, R(39, 56, 18, 18));
            graphics.FillEllipse(brush, R(73, 56, 18, 18));
            using var mouth = new GraphicsPath();
            mouth.AddBezier(P(54, 86), P(58, 76), P(72, 76), P(77, 86));
            mouth.AddLine(P(77, 90), P(54, 90));
            mouth.CloseFigure();
            graphics.FillPath(brush, mouth);
        }
    }

    private static GraphicsPath RoundRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawPixelSparkles(Graphics g, float s)
    {
        using var cyan = new SolidBrush(Color.FromArgb(210, 25, 160, 255));
        using var purple = new SolidBrush(Color.FromArgb(170, 148, 37, 255));
        var pixels = new (Brush Brush, float X, float Y, float W, float H)[]
        {
            (cyan, 105, 18, 3, 5),
            (purple, 17, 18, 4, 4),
            (purple, 109, 88, 4, 9),
            (cyan, 7, 83, 4, 4),
            (purple, 17, 101, 5, 3),
            (purple, 101, 8, 3, 3),
            (cyan, 97, 104, 7, 3)
        };

        foreach (var pixel in pixels)
        {
            g.FillRectangle(pixel.Brush, pixel.X * s, pixel.Y * s, pixel.W * s, pixel.H * s);
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}
