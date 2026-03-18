using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameTransfer.App.Helpers;

public static class IconHelper
{
    private static readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _defaultIcon;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static ImageSource? GetGameIcon(string? installPath, string? executablePath)
    {
        // Try executable path first
        if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
        {
            return GetIconFromFile(executablePath);
        }

        // Try to find an exe in the install directory
        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
        {
            return GetIconFromDirectory(installPath);
        }

        return GetDefaultIcon();
    }

    private static ImageSource? GetIconFromFile(string filePath)
    {
        if (_cache.TryGetValue(filePath, out var cached))
            return cached ?? GetDefaultIcon();

        try
        {
            var hIcon = ExtractIcon(IntPtr.Zero, filePath, 0);
            if (hIcon != IntPtr.Zero && hIcon.ToInt64() != 1)
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));
                bitmapSource.Freeze();
                DestroyIcon(hIcon);
                _cache[filePath] = bitmapSource;
                return bitmapSource;
            }

            if (hIcon != IntPtr.Zero)
                DestroyIcon(hIcon);
        }
        catch
        {
            // Ignore icon extraction failures
        }

        _cache[filePath] = null;
        return GetDefaultIcon();
    }

    private static ImageSource? GetIconFromDirectory(string directoryPath)
    {
        if (_cache.TryGetValue(directoryPath, out var cached))
            return cached ?? GetDefaultIcon();

        try
        {
            var exeFiles = Directory.GetFiles(directoryPath, "*.exe", SearchOption.TopDirectoryOnly);

            var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "unins000.exe", "uninstall.exe", "crashreporter.exe", "crashhandler.exe",
                "dotnetfx.exe", "vcredist.exe", "vcredist_x86.exe", "vcredist_x64.exe",
                "dxsetup.exe", "UnityCrashHandler64.exe", "UnityCrashHandler32.exe",
                "CrashReportClient.exe", "EasyAntiCheat_Setup.exe"
            };

            var bestExe = exeFiles
                .Where(f => !skipNames.Contains(Path.GetFileName(f)))
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();

            if (bestExe != null)
            {
                var result = GetIconFromFile(bestExe);
                _cache[directoryPath] = result;
                return result;
            }
        }
        catch
        {
            // Ignore directory scan failures
        }

        _cache[directoryPath] = null;
        return GetDefaultIcon();
    }

    private static ImageSource? GetDefaultIcon()
    {
        if (_defaultIcon != null)
            return _defaultIcon;

        try
        {
            var drawing = new DrawingGroup();
            using (var ctx = drawing.Open())
            {
                var brush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                brush.Freeze();
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(140, 140, 140)), 1);
                pen.Freeze();
                ctx.DrawRoundedRectangle(brush, pen, new Rect(2, 2, 20, 20), 3, 3);

                var textBrush = new SolidColorBrush(Colors.White);
                textBrush.Freeze();
                var text = new FormattedText("🎮",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Emoji"),
                    12, textBrush, 1.0);
                ctx.DrawText(text, new Point(12 - text.Width / 2, 12 - text.Height / 2));
            }
            drawing.Freeze();

            var image = new DrawingImage(drawing);
            image.Freeze();
            _defaultIcon = image;
            return _defaultIcon;
        }
        catch
        {
            return null;
        }
    }
}
