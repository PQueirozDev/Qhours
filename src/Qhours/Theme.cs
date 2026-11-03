using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Qhours;

/// <summary>Paleta, fontes e utilitários visuais (tema escuro).</summary>
static class Theme
{
    public static readonly Color Bg = Color.FromArgb(13, 14, 17);
    public static readonly Color Surface = Color.FromArgb(21, 23, 28);
    public static readonly Color Input = Color.FromArgb(29, 32, 39);
    public static readonly Color Border = Color.FromArgb(37, 40, 48);
    public static readonly Color BorderStrong = Color.FromArgb(55, 59, 70);
    public static readonly Color Text = Color.FromArgb(236, 237, 239);
    public static readonly Color Muted = Color.FromArgb(140, 145, 156);
    public static readonly Color Faint = Color.FromArgb(96, 101, 112);
    public static readonly Color Accent = Color.FromArgb(240, 168, 72);
    public static readonly Color AccentHover = Color.FromArgb(247, 184, 98);
    public static readonly Color AccentInk = Color.FromArgb(28, 20, 10);
    public static readonly Color Green = Color.FromArgb(74, 199, 112);
    public static readonly Color Orange = Color.FromArgb(240, 136, 62);
    public static readonly Color Blue = Color.FromArgb(110, 168, 254);
    public static readonly Color Red = Color.FromArgb(242, 95, 92);

    public static Font Regular(float size) => new("Segoe UI", size, FontStyle.Regular);
    public static Font Semibold(float size) => new("Segoe UI Semibold", size, FontStyle.Regular);
    public static Font Bold(float size) => new("Segoe UI", size, FontStyle.Bold);

    static Icon? appIcon;
    public static Icon AppIcon => appIcon ??= LoadIcon();

    static Icon LoadIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);

    /// <summary>Barra de título escura (Windows 10 20H1+ / 11) e cor da legenda no Windows 11.</summary>
    public static void DarkTitleBar(IntPtr hwnd)
    {
        try
        {
            var on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            var caption = ColorTranslator.ToWin32(Bg);
            DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
        }
        catch { }
    }

    /// <summary>Barras de rolagem escuras em listas.</summary>
    public static void DarkScrollbars(IntPtr hwnd)
    {
        try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); } catch { }
    }
}
