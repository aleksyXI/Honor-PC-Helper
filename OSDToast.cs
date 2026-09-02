using System.Drawing.Drawing2D;
using System.Reflection;
namespace HonorPCHelper;
internal sealed class OSDToast : OSDNativeForm, IDisposable
{
    private const float BaseW = 230f;
    private const float BaseH = 220f;
    private const float BaseIcon = 98f;
    private const float BaseTitle = 13f;
    private const float BaseDesc = 9f;
    private const int BaseRadius = 14;
    private readonly float _scale;
    private Font _titleFont = null!;
    private Font _descFont = null!;
    private Font _iconFont = null!;
    private static readonly SolidBrush PlateBrush = new(Color.FromArgb(205, 20, 20, 20));
    private static readonly SolidBrush TextBrush = new(Color.White);
    private static readonly StringFormat CenterFormat = new()
    {
        LineAlignment = StringAlignment.Center,
        Alignment = StringAlignment.Center
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2200 };
    private string _icon = "";
    private string _title = "";
    private string _desc = "";
    private Bitmap? _bitmap;
    private static readonly Dictionary<string, Bitmap> Cache = new();
    internal OSDToast()
    {
        User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        using (var g = Graphics.FromHwnd(IntPtr.Zero))
            _scale = g.DpiX / 96f;
        _titleFont = new Font("Segoe UI", BaseTitle * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _descFont = new Font("Segoe UI", BaseDesc * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _iconFont = new Font("Segoe UI Emoji", 56f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _timer.Tick += (_, _) => { _timer.Stop(); Hide(); };
        Alpha = 235;
        Size = new Size((int)(BaseW * _scale), (int)(BaseH * _scale));
    }
    internal void Show(string? iconName, string emojiFallback, string title, string desc = "")
    {
        _bitmap = iconName is null ? null : LoadIcon(iconName);
        _icon = _bitmap is null ? emojiFallback : "";
        _title = title;
        _desc = desc;
        _timer.Stop();
        var screen = Screen.PrimaryScreen;
        if (screen is not null)
        {
            X = screen.Bounds.Left + (screen.Bounds.Width - (int)(BaseW * _scale)) / 2;
            Y = screen.Bounds.Bottom - (int)(96 * _scale) - (int)(BaseH * _scale);
        }
        Show();
        _timer.Start();
    }
    private static Bitmap? LoadIcon(string name)
    {
        if (Cache.TryGetValue(name, out var cached))
            return cached;
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream($"HonorPCHelper.OSDIcons.{name}.png");
            if (stream is null)
                return null;
            using (stream)
            {
                var bitmap = new Bitmap(stream);
                Cache[name] = bitmap;
                return bitmap;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error($"OSD icon '{name}' load failed", exception);
            return null;
        }
    }
    protected override void PerformPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int w = (int)(BaseW * _scale), h = (int)(BaseH * _scale);
        using var path = RoundedRect(new Rectangle(0, 0, w, h), (int)(BaseRadius * _scale));
        e.Graphics.FillPath(PlateBrush, path);
        int iconSize = (int)(BaseIcon * _scale);
        var iconBottom = (int)(16 * _scale) + iconSize;
        if (_bitmap is not null)
        {
            var iconRect = new Rectangle((w - iconSize) / 2, (int)(16 * _scale), iconSize, iconSize);
            e.Graphics.DrawImage(_bitmap, iconRect);
        }
        else if (_icon.Length > 0)
        {
            e.Graphics.DrawString(_icon, _iconFont, TextBrush, new RectangleF(0, (int)(6 * _scale), w, iconSize), CenterFormat);
        }
        var inner = new RectangleF(14 * _scale, 0, w - 28 * _scale, h - 24 * _scale);
        if (_desc.Length == 0)
        {
            if (_title.Length > 0)
            {
                var remaining = h - 24 * _scale - iconBottom;
                var titleRect = new RectangleF(inner.X, iconBottom + remaining / 2f - 20 * _scale, inner.Width, 40 * _scale);
                e.Graphics.DrawString(_title, _titleFont, TextBrush, titleRect, CenterFormat);
            }
        }
        else
        {
            if (_title.Length > 0)
                e.Graphics.DrawString(_title, _titleFont, TextBrush,
                    new RectangleF(inner.X, iconBottom + 12 * _scale, inner.Width, 38 * _scale), CenterFormat);
            e.Graphics.DrawString(_desc, _descFont, TextBrush,
                new RectangleF(inner.X, iconBottom + 54 * _scale, inner.Width, 50 * _scale), CenterFormat);
        }
    }
    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        var path = new GraphicsPath();
        if (radius == 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
    public void Dispose()
    {
        _titleFont.Dispose();
        _descFont.Dispose();
        _iconFont.Dispose();
        _timer.Dispose();
        base.Dispose();
    }
}
