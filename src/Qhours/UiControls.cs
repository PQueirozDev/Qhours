using System.Drawing.Drawing2D;

namespace Qhours;

/// <summary>Painel com cantos arredondados e borda fina.</summary>
sealed class Card : Panel
{
    public int Radius { get; set; } = 12;

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius);
        using var fill = new SolidBrush(BackColor);
        using var pen = new Pen(Theme.Border);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }
}

enum ButtonKind { Primary, Secondary, Danger }

/// <summary>Botão desenhado à mão: primário (âmbar), secundário (contorno) ou perigo.</summary>
sealed class RoundButton : Control
{
    bool hover, down;
    public ButtonKind Kind { get; set; } = ButtonKind.Secondary;

    public RoundButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Font = Theme.Semibold(9.5f);
        Cursor = Cursors.Hand;
        TabStop = true;
        Size = new Size(120, 36);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; hover = down = false; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space && Enabled) OnClick(EventArgs.Empty);
        base.OnKeyUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fill, border, text;
        switch (Kind)
        {
            case ButtonKind.Primary:
                fill = down ? Theme.Mix(Theme.Accent, Color.Black, 0.12f) : hover ? Theme.AccentHover : Theme.Accent;
                border = fill;
                text = Theme.AccentInk;
                break;
            case ButtonKind.Danger:
                fill = hover ? Theme.Mix(Theme.Surface, Theme.Red, 0.16f) : Theme.Surface;
                border = Theme.Mix(Theme.Surface, Theme.Red, 0.55f);
                text = Theme.Red;
                break;
            default:
                fill = down ? Theme.Mix(Theme.Input, Color.White, 0.04f) : hover ? Theme.Mix(Theme.Input, Color.White, 0.06f) : Theme.Input;
                border = hover ? Theme.BorderStrong : Theme.Border;
                text = Theme.Text;
                break;
        }

        if (!Enabled)
        {
            var bg = Parent?.BackColor ?? Theme.Surface;
            fill = Theme.Mix(fill, bg, 0.6f);
            border = Theme.Mix(border, bg, 0.6f);
            text = Theme.Mix(text, bg, 0.55f);
        }

        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(border)) g.DrawPath(p, path);

        if (Focused && ShowFocusCues)
        {
            using var ring = Theme.Rounded(new RectangleF(2.5f, 2.5f, Width - 5.5f, Height - 5.5f), 6);
            using var fp = new Pen(Kind == ButtonKind.Primary ? Theme.AccentInk : Theme.Accent) { DashStyle = DashStyle.Dot };
            g.DrawPath(fp, ring);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Caixa de texto escura com borda arredondada e foco em âmbar.</summary>
sealed class InputBox : Control
{
    public TextBox Box { get; } = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Input,
        ForeColor = Theme.Text,
    };

    bool invalid;
    public bool Invalid { get => invalid; set { invalid = value; Invalidate(); } }

    public InputBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(200, 36);
        Box.Font = Theme.Regular(10f);
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Box.EnabledChanged += (_, _) => { Box.BackColor = Theme.Input; Invalidate(); };
        Cursor = Cursors.IBeam;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }
    public string Placeholder { get => Box.PlaceholderText; set => Box.PlaceholderText = value; }

    protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }
    protected override void OnEnabledChanged(EventArgs e) { Box.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var pad = (int)(12 * DeviceDpi / 96f);
        Box.SetBounds(pad, (Height - Box.Height) / 2, Width - pad * 2, Box.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var border = invalid ? Theme.Red : Box.Focused ? Theme.Accent : Theme.Border;
        var fill = Enabled ? Theme.Input : Theme.Mix(Theme.Input, Theme.Surface, 0.5f);
        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(border)) g.DrawPath(p, path);
    }
}

/// <summary>Barra de progresso fina.</summary>
sealed class ProgressLine : Control
{
    double value;
    public double Value { get => value; set { this.value = Math.Clamp(value, 0, 1); Invalidate(); } }

    public ProgressLine()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 6;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = Height / 2f;
        using (var track = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), r))
        using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, track);
        var w = (float)((Width - 1) * value);
        if (w >= 1)
        {
            using var bar = Theme.Rounded(new RectangleF(0, 0, Math.Max(w, Height), Height - 1), r);
            using var b = new SolidBrush(Theme.Accent);
            g.FillPath(b, bar);
        }
    }
}

/// <summary>Selo de status: ponto colorido + texto, com fundo levemente tingido.</summary>
sealed class StatusPill : Control
{
    Color tone = Theme.Muted;
    public Color Tone { get => tone; set { tone = value; Invalidate(); } }

    public StatusPill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Semibold(8.5f);
        Height = 26;
    }

    /// <summary>Largura ideal para o texto atual, em pixels do monitor atual.</summary>
    public int PreferredWidth
    {
        get
        {
            var s = DeviceDpi / 96f;
            return TextRenderer.MeasureText(Text, Font).Width + (int)(30 * s);
        }
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var bg = Parent?.BackColor ?? Theme.Surface;
        g.Clear(bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var s = DeviceDpi / 96f;
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), (Height - 1) / 2f))
        {
            using var b = new SolidBrush(Theme.Mix(bg, tone, 0.14f));
            using var p = new Pen(Theme.Mix(bg, tone, 0.35f));
            g.FillPath(b, path);
            g.DrawPath(p, path);
        }
        var d = 7 * s;
        using (var dot = new SolidBrush(tone)) g.FillEllipse(dot, 11 * s, (Height - d) / 2f, d, d);
        var textRect = new Rectangle((int)(23 * s), 0, Width - (int)(23 * s), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Theme.Mix(tone, Theme.Text, 0.35f),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
    }
}

/// <summary>Capa do jogo com cantos arredondados; sem imagem, mostra um degradê com o nome.</summary>
sealed class GameBanner : Control
{
    Image? image;
    public Image? Image
    {
        get => image;
        set { var old = image; image = value; if (!ReferenceEquals(old, value)) old?.Dispose(); Invalidate(); }
    }

    public GameBanner()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Semibold(13f);
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 8);
        g.SetClip(path);
        if (image != null)
        {
            // Preenche cortando o excesso, mantendo a proporção
            var scale = Math.Max((float)Width / image.Width, (float)Height / image.Height);
            var w = image.Width * scale; var h = image.Height * scale;
            g.DrawImage(image, (Width - w) / 2, (Height - h) / 2, w, h);
        }
        else
        {
            using var grad = new LinearGradientBrush(ClientRectangle, Theme.Mix(Theme.Input, Theme.Accent, 0.18f), Theme.Input, 35f);
            g.FillRectangle(grad, ClientRectangle);
            TextRenderer.DrawText(g, Text, Font, Rectangle.Inflate(ClientRectangle, -16, -16), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
        g.ResetClip();
        using var pen = new Pen(Color.FromArgb(40, 255, 255, 255));
        g.DrawPath(pen, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) image?.Dispose();
        base.Dispose(disposing);
    }
}

sealed record LogEntry(DateTime When, string Message, Color Tone);

/// <summary>Lista de atividade com horário apagado e mensagem com quebra de linha.</summary>
sealed class LogList : ListBox
{
    public LogList()
    {
        DrawMode = DrawMode.OwnerDrawVariable;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Regular(8.75f);
        IntegralHeight = false;
        SelectionMode = SelectionMode.None;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkScrollbars(Handle);
    }

    int TimeWidth => TextRenderer.MeasureText("00/00 00:00", Font).Width + (int)(8 * DeviceDpi / 96f);

    protected override void OnMeasureItem(MeasureItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not LogEntry it) return;
        var w = Math.Max(50, ClientSize.Width - TimeWidth - 4);
        var size = TextRenderer.MeasureText(e.Graphics, it.Message, Font, new Size(w, 0), TextFormatFlags.WordBreak);
        e.ItemHeight = Math.Min(255, size.Height + (int)(6 * DeviceDpi / 96f));
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using (var bg = new SolidBrush(BackColor)) e.Graphics.FillRectangle(bg, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not LogEntry it) return;
        var tw = TimeWidth;
        var top = e.Bounds.Top + (int)(3 * DeviceDpi / 96f);
        TextRenderer.DrawText(e.Graphics, it.When.ToString("dd/MM HH:mm"), Font, new Point(e.Bounds.Left, top), Theme.Faint);
        var rect = new Rectangle(e.Bounds.Left + tw, top, e.Bounds.Width - tw - 4, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, it.Message, Font, rect, it.Tone, TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);
    }
}

/// <summary>Rótulos pré-configurados.</summary>
static class Ui
{
    public static Label Caption(string text) => new()
    {
        Text = text.ToUpperInvariant(), AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Semibold(8f),
    };

    public static Label Text(string text, float size = 9.5f, Color? color = null, bool semibold = false) => new()
    {
        Text = text, AutoSize = false, AutoEllipsis = true, ForeColor = color ?? Theme.Text,
        Font = semibold ? Theme.Semibold(size) : Theme.Regular(size),
    };
}
