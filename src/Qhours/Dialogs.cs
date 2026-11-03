namespace Qhours;

/// <summary>Diálogos no tema escuro (substituem o MessageBox padrão).</summary>
static class Dialogs
{
    public static void Info(IWin32Window? owner, string title, string message) =>
        Show(owner, title, message, "OK", null, input: false, out _);

    public static bool Confirm(IWin32Window? owner, string title, string message, string yes, string no) =>
        Show(owner, title, message, yes, no, input: false, out _);

    public static string? Prompt(IWin32Window? owner, string title, string message, string ok = "Confirmar") =>
        Show(owner, title, message, ok, "Cancelar", input: true, out var value) ? value : null;

    static bool Show(IWin32Window? owner, string title, string message, string okText, string? cancelText, bool input, out string value)
    {
        const int W = 420, Pad = 22;
        using var f = new DialogForm
        {
            Text = title,
            StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
        };
        f.SuspendLayout();
        f.AutoScaleDimensions = new SizeF(96F, 96F);
        f.AutoScaleMode = AutoScaleMode.Dpi;

        var titleLbl = new Label { Text = title, Font = Theme.Semibold(12f), ForeColor = Theme.Text, AutoSize = false, Left = Pad, Top = Pad, Width = W - 2 * Pad, Height = 26 };
        var msgFont = Theme.Regular(9.5f);
        var msgH = TextRenderer.MeasureText(message, msgFont, new Size(W - 2 * Pad, 0), TextFormatFlags.WordBreak).Height;
        var msgLbl = new Label { Text = message, Font = msgFont, ForeColor = Theme.Muted, AutoSize = false, Left = Pad, Top = Pad + 34, Width = W - 2 * Pad, Height = msgH + 4 };
        f.Controls.AddRange([titleLbl, msgLbl]);

        var y = msgLbl.Bottom + 14;
        InputBox? box = null;
        if (input)
        {
            box = new InputBox { Left = Pad, Top = y, Width = W - 2 * Pad, Height = 38 };
            box.Box.CharacterCasing = CharacterCasing.Upper;
            box.Box.MaxLength = 10;
            box.Box.Font = Theme.Semibold(12f);
            f.Controls.Add(box);
            y = box.Bottom + 16;
        }
        else y += 6;

        var ok = new RoundButton { Text = okText, Kind = ButtonKind.Primary, Width = 150, Height = 36, Top = y };
        ok.Left = W - Pad - ok.Width;
        ok.Click += (_, _) => { f.DialogResult = DialogResult.OK; f.Close(); };
        f.Controls.Add(ok);
        if (cancelText != null)
        {
            var cancel = new RoundButton { Text = cancelText, Kind = ButtonKind.Secondary, Width = 150, Height = 36, Top = y };
            cancel.Left = ok.Left - 10 - cancel.Width;
            cancel.Click += (_, _) => { f.DialogResult = DialogResult.Cancel; f.Close(); };
            f.Controls.Add(cancel);
        }
        f.ClientSize = new Size(W, y + 36 + Pad);
        f.ResumeLayout(false);

        f.Shown += (_, _) => { if (box != null) box.Box.Focus(); else ok.Focus(); };
        f.KeyPreview = true;
        f.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && f.ActiveControl is RoundButton rb && rb != ok) return; // Enter no "Cancelar" = cancelar
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; f.DialogResult = DialogResult.OK; f.Close(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; f.DialogResult = DialogResult.Cancel; f.Close(); }
        };

        var result = owner == null ? f.ShowDialog() : f.ShowDialog(owner);
        value = box?.Text.Trim() ?? "";
        return result == DialogResult.OK;
    }

    sealed class DialogForm : Form
    {
        public DialogForm()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = Theme.Regular(9.5f);
            Icon = Theme.AppIcon;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }
    }
}
