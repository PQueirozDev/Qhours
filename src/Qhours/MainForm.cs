using System.Diagnostics;
using System.Globalization;

namespace Qhours;

public sealed class MainForm : Form
{
    const string AppTitle = "Qhours";

    readonly Storage storage = new();
    readonly SteamService steam;
    readonly Storage.Settings cfg;
    readonly GameInfoService games;

    // Jogo
    readonly GameBanner banner = new();
    readonly Label lblGame = Ui.Text("", 12f, semibold: true);
    readonly Label lblAppId = Ui.Text("", 8.75f, Theme.Muted);
    readonly InputBox txtAppId = new() { Placeholder = "AppID (ex.: 1174180)" };
    readonly RoundButton btnGame = new() { Text = "Trocar jogo" };
    readonly Label lblGameHint = Ui.Text("Número da URL da loja (…/app/1174180) ou cole o link.", 8.5f, Theme.Faint);

    // Conta
    readonly Panel pnlLogin = new();
    readonly Panel pnlSaved = new();
    readonly InputBox txtUser = new() { Placeholder = "Usuário da Steam" };
    readonly InputBox txtPass = new() { Placeholder = "Senha" };
    readonly Label lblSavedUser = Ui.Text("", 11f, semibold: true);
    readonly RoundButton btnForget = new() { Text = "Esquecer conta", Kind = ButtonKind.Danger };

    // Sessão
    readonly StatusPill pill = new();
    readonly Label lblTime = new() { AutoSize = false, Font = Theme.Semibold(34f), ForeColor = Theme.Text, Text = "00:00:00", TextAlign = ContentAlignment.MiddleLeft };
    readonly Label lblTotal = Ui.Text("", 9f, Theme.Muted);
    readonly RoundButton btnStart = new() { Text = "Iniciar", Kind = ButtonKind.Primary };
    readonly RoundButton btnStop = new() { Text = "Parar", Enabled = false };

    // Meta
    readonly InputBox txtGoal = new() { Placeholder = "Horas" };
    readonly RoundButton btnGoal = new() { Text = "Definir" };
    readonly Label lblGoal = Ui.Text("", 9f, Theme.Muted);
    readonly ProgressLine barGoal = new();

    readonly LogList log = new();
    readonly NotifyIcon tray = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly Stopwatch clock = Stopwatch.StartNew();

    TimeSpan lastTick;
    double sessionSeconds;
    int ticksSinceSave;
    bool exiting;
    bool hasSavedSession;
    bool trayHintShown;
    bool changingGame;
    int gameLookup;

    public MainForm()
    {
        cfg = storage.LoadSettings();
        games = new GameInfoService(storage.CacheDir);
        steam = new SteamService(storage, new GuiAuthenticator(this));
        steam.Log += msg => PostLog(msg);
        steam.StateChanged += () => BeginInvokeSafe(() => { if (steam.IsLoggedOn && !hasSavedSession) LoadAccountUi(); RefreshUi(); });
        steam.Fatal += msg => BeginInvokeSafe(() =>
        {
            StopSteam();
            Dialogs.Info(this, AppTitle, msg);
        });

        BuildLayout();

        btnStart.Click += (_, _) => StartSteam();
        btnStop.Click += (_, _) => StopSteam();
        btnForget.Click += (_, _) => ForgetAccount();
        btnGoal.Click += (_, _) => SetGoal();
        btnGame.Click += async (_, _) => await ChangeGameAsync();
        txtAppId.Box.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ChangeGameAsync(); } };
        txtAppId.Box.TextChanged += (_, _) => txtAppId.Invalid = false;
        txtGoal.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SetGoal(); } };
        txtGoal.Box.TextChanged += (_, _) => txtGoal.Invalid = false;
        txtPass.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; StartSteam(); } };
        timer.Tick += (_, _) => Tick();

        tray.Icon = Theme.AppIcon;
        tray.Text = AppTitle;
        tray.Visible = true;
        tray.DoubleClick += (_, _) => RestoreFromTray();
        tray.ContextMenuStrip = new ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add("Abrir", null, (_, _) => RestoreFromTray());
        tray.ContextMenuStrip.Items.Add("Parar e sair", null, (_, _) => { exiting = true; Close(); });

        Resize += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized) return;
            Hide();
            if (!trayHintShown && steam.IsRunning)
            {
                trayHintShown = true;
                tray.ShowBalloonTip(5000, AppTitle, "Continua contando aqui na bandeja. Clique duas vezes para abrir.", ToolTipIcon.Info);
            }
        };
        FormClosing += OnClosing;

        ApplyGame(cfg.AppId, cfg.AppName);
        lastTick = clock.Elapsed;
        timer.Start();
        LoadAccountUi();
        RefreshUi();
        PostLog("Pronto. O Qhours não abre o jogo e não altera arquivos dele.", Theme.Muted);
        _ = LoadGameAsync(cfg.AppId);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    // ---------- Layout (coordenadas em 96 DPI; o WinForms escala para o monitor) ----------

    void BuildLayout()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = AppTitle;
        Icon = Theme.AppIcon;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Regular(9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(880, 628);
        DoubleBuffered = true;

        // Cabeçalho
        var logo = new PictureBox { Image = Theme.AppIcon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new(24, 22, 34, 34) };
        var brand = Ui.Text(AppTitle, 15f, semibold: true); brand.SetBounds(66, 14, 200, 30);
        var tagline = Ui.Text("Horas na Steam sem abrir o jogo", 9f, Theme.Muted); tagline.SetBounds(67, 42, 300, 18);
        var version = Ui.Text("v" + Application.ProductVersion.Split('+')[0], 8.5f, Theme.Faint);
        version.TextAlign = ContentAlignment.MiddleRight; version.SetBounds(656, 28, 200, 18);
        Controls.AddRange([logo, brand, tagline, version]);

        // Coluna esquerda: jogo
        var gameCard = new Card { Bounds = new(24, 80, 400, 306) };
        banner.SetBounds(16, 16, 368, 172);
        lblGame.SetBounds(16, 194, 368, 26);
        lblAppId.SetBounds(16, 220, 368, 18);
        txtAppId.SetBounds(16, 246, 210, 36);
        btnGame.SetBounds(234, 246, 150, 36);
        lblGameHint.SetBounds(16, 284, 368, 16);
        gameCard.Controls.AddRange([banner, lblGame, lblAppId, txtAppId, btnGame, lblGameHint]);

        // Coluna esquerda: conta
        var accCard = new Card { Bounds = new(24, 400, 400, 192) };
        var accCap = Ui.Caption("Conta Steam"); accCap.Location = new(16, 16);
        pnlLogin.SetBounds(16, 40, 368, 140);
        txtUser.SetBounds(0, 0, 368, 36);
        txtPass.SetBounds(0, 44, 368, 36);
        txtPass.Box.UseSystemPasswordChar = true;
        var passNote = Ui.Text("A senha não é gravada: fica só um token criptografado pelo Windows (DPAPI). O Steam Guard é pedido no 1º login.", 8.5f, Theme.Faint);
        passNote.SetBounds(0, 88, 368, 48);
        pnlLogin.Controls.AddRange([txtUser, txtPass, passNote]);

        pnlSaved.SetBounds(16, 40, 368, 140);
        lblSavedUser.SetBounds(0, 0, 368, 24);
        var savedNote = Ui.Text("Sessão salva neste PC. Não precisa digitar a senha de novo.", 8.75f, Theme.Muted);
        savedNote.SetBounds(0, 26, 368, 36);
        btnForget.SetBounds(0, 70, 150, 34);
        pnlSaved.Controls.AddRange([lblSavedUser, savedNote, btnForget]);
        accCard.Controls.AddRange([accCap, pnlLogin, pnlSaved]);

        // Coluna direita: sessão
        var sessCard = new Card { Bounds = new(440, 80, 416, 206) };
        var sessCap = Ui.Caption("Sessão atual"); sessCap.Location = new(18, 18);
        pill.SetBounds(270, 14, 130, 26);
        lblTime.SetBounds(12, 42, 392, 62);
        lblTotal.SetBounds(18, 106, 380, 20);
        btnStart.SetBounds(18, 146, 186, 42);
        btnStop.SetBounds(212, 146, 186, 42);
        btnStart.Font = btnStop.Font = Theme.Semibold(10.5f);
        sessCard.Controls.AddRange([sessCap, pill, lblTime, lblTotal, btnStart, btnStop]);

        // Coluna direita: meta
        var goalCard = new Card { Bounds = new(440, 300, 416, 132) };
        var goalCap = Ui.Caption("Meta de horas"); goalCap.Location = new(18, 16);
        txtGoal.SetBounds(18, 40, 120, 36);
        btnGoal.SetBounds(146, 40, 100, 36);
        var goalHint = Ui.Text("Para sozinho ao somar essas horas.", 8.5f, Theme.Faint);
        goalHint.SetBounds(256, 40, 144, 36);
        lblGoal.SetBounds(18, 86, 380, 20);
        barGoal.SetBounds(18, 110, 380, 6);
        goalCard.Controls.AddRange([goalCap, txtGoal, btnGoal, goalHint, lblGoal, barGoal]);

        // Coluna direita: atividade
        var logCard = new Card { Bounds = new(440, 446, 416, 146) };
        var logCap = Ui.Caption("Atividade"); logCap.Location = new(18, 14);
        log.SetBounds(16, 36, 388, 100);
        logCard.Controls.AddRange([logCap, log]);

        Controls.AddRange([gameCard, accCard, sessCard, goalCard, logCard]);

        var foot = Ui.Text("A Steam grava o tempo de jogo de hora em hora. Para ver o valor novo, feche e reabra o cliente Steam.", 8.5f, Theme.Faint);
        foot.SetBounds(24, 602, 832, 18);
        Controls.Add(foot);
        ResumeLayout(false);
    }

    // ---------- Jogo ----------

    void ApplyGame(uint appId, string? name)
    {
        var display = string.IsNullOrWhiteSpace(name) ? $"AppID {appId}" : name!;
        lblGame.Text = display;
        lblAppId.Text = $"AppID {appId}";
        banner.Text = display;
        steam.AppId = appId;
        steam.GameName = display;
        Text = $"{AppTitle} · {display}";
        RefreshTotals();
    }

    async Task LoadGameAsync(uint appId)
    {
        var ticket = ++gameLookup;
        var info = await games.GetAsync(appId);
        if (ticket != gameLookup || appId != cfg.AppId || IsDisposed) { info.Cover?.Dispose(); return; }
        if (!string.IsNullOrWhiteSpace(info.Name) && info.Name != cfg.AppName)
        {
            cfg.AppName = info.Name;
            storage.SaveSettings(cfg);
        }
        ApplyGame(appId, cfg.AppName);
        banner.Image = info.Cover;
    }

    async Task ChangeGameAsync()
    {
        if (steam.IsRunning || changingGame) return;
        var raw = txtAppId.Text.Trim();
        // Aceita também o link da loja colado inteiro
        var m = System.Text.RegularExpressions.Regex.Match(raw, @"(?:/app/)?(\d{1,10})");
        if (!m.Success || !uint.TryParse(m.Groups[1].Value, out var id) || id == 0)
        {
            txtAppId.Invalid = true;
            PostLog("AppID inválido. Use só os números (ex.: 1174180) ou cole o link da loja.", Theme.Red);
            return;
        }
        if (id == cfg.AppId) { txtAppId.Text = ""; return; }

        changingGame = true;
        btnGame.Enabled = false;
        btnGame.Text = "Buscando...";
        GameInfoService.Info info;
        try { info = await games.GetAsync(id); }
        finally { changingGame = false; btnGame.Text = "Trocar jogo"; }
        if (IsDisposed) return;
        if (steam.IsRunning) { info.Cover?.Dispose(); RefreshUi(); return; }

        cfg.AppId = id;
        cfg.AppName = info.Name;
        cfg.MetaHoras = 0;
        cfg.MetaInicioSegundos = 0;
        storage.SaveSettings(cfg);
        gameLookup++;
        ApplyGame(id, info.Name);
        banner.Image = info.Cover;
        txtAppId.Text = "";
        PostLog(info.FoundInStore
            ? $"Jogo trocado: {info.Name} ({id}). Você precisa ter este jogo na sua biblioteca."
            : $"AppID {id} não foi encontrado na loja (pode ser um app oculto). Ele só conta horas se estiver na sua biblioteca.",
            info.FoundInStore ? Theme.Text : Theme.Orange);
        RefreshUi();
    }

    // ---------- Conta / sessão ----------

    void LoadAccountUi()
    {
        var acc = storage.LoadAccount();
        hasSavedSession = acc != null && storage.LoadToken() != null;
        if (acc != null) txtUser.Text = acc.Username;
        lblSavedUser.Text = acc?.Username ?? "";
        pnlSaved.Visible = hasSavedSession;
        pnlLogin.Visible = !hasSavedSession;
    }

    void StartSteam()
    {
        if (steam.IsRunning) return;
        var hasToken = storage.LoadAccount() != null && storage.LoadToken() != null;
        if (!hasToken && (string.IsNullOrWhiteSpace(txtUser.Text) || string.IsNullOrEmpty(txtPass.Text)))
        {
            txtUser.Invalid = string.IsNullOrWhiteSpace(txtUser.Text);
            txtPass.Invalid = string.IsNullOrEmpty(txtPass.Text);
            PostLog("Informe usuário e senha da Steam para o primeiro login.", Theme.Orange);
            return;
        }
        txtUser.Invalid = txtPass.Invalid = false;
        var pass = hasToken ? null : txtPass.Text;
        txtPass.Text = "";
        sessionSeconds = 0;
        steam.Start(txtUser.Text.Trim(), pass);
        RefreshUi();
    }

    void StopSteam()
    {
        if (steam.IsRunning)
        {
            Cursor = Cursors.WaitCursor;
            steam.Stop();
            Cursor = Cursors.Default;
        }
        storage.SaveSettings(cfg);
        LoadAccountUi();
        RefreshUi();
    }

    void ForgetAccount()
    {
        if (!Dialogs.Confirm(this, "Esquecer conta",
                "Apagar a sessão salva deste PC? Você vai precisar digitar a senha e confirmar o Steam Guard de novo.",
                "Apagar sessão", "Cancelar")) return;
        StopSteam();
        storage.ForgetAccount();
        txtUser.Text = "";
        LoadAccountUi();
        PostLog("Sessão salva apagada deste PC.");
        RefreshUi();
    }

    void SetGoal()
    {
        var raw = txtGoal.Text.Trim().Replace(',', '.');
        if (raw.Length == 0) raw = "0";
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) || !double.IsFinite(hours) || hours < 0 || hours > 100000)
        {
            txtGoal.Invalid = true;
            return;
        }
        cfg.MetaHoras = hours;
        cfg.MetaInicioSegundos = cfg.TotalAtual;
        storage.SaveSettings(cfg);
        txtGoal.Text = "";
        PostLog(hours > 0 ? $"Meta definida: +{hours:0.#} h a partir de agora." : "Meta removida.");
        RefreshUi();
    }

    void Tick()
    {
        var now = clock.Elapsed;
        var delta = (now - lastTick).TotalSeconds;
        lastTick = now;
        if (delta > 5) delta = 0; // PC suspenso: não conta

        if (steam.IsFarming)
        {
            sessionSeconds += delta;
            cfg.TotalAtual += delta;
        }

        if (++ticksSinceSave >= 60)
        {
            ticksSinceSave = 0;
            storage.SaveSettings(cfg);
        }

        if (cfg.MetaHoras > 0 && cfg.TotalAtual - cfg.MetaInicioSegundos >= cfg.MetaHoras * 3600)
        {
            var meta = cfg.MetaHoras;
            cfg.MetaHoras = 0;
            StopSteam();
            PostLog($"Meta atingida (+{meta:0.#} h). Parado automaticamente.", Theme.Green);
            tray.ShowBalloonTip(10000, AppTitle, $"Meta de +{meta:0.#} h atingida. Parado.", ToolTipIcon.Info);
        }

        RefreshUi();
    }

    void RefreshUi()
    {
        var running = steam.IsRunning;
        btnStart.Enabled = !running;
        btnStop.Enabled = running;
        btnForget.Enabled = !running;
        btnGame.Enabled = txtAppId.Enabled = !running && !changingGame;
        txtUser.Enabled = txtPass.Enabled = !running;

        var (state, tone) =
            !running ? ("Parado", Theme.Muted)
            : steam.IsBlocked ? ("Pausado", Theme.Orange)
            : steam.IsFarming ? ("Contando", Theme.Green)
            : steam.IsLoggedOn ? ("Iniciando", Theme.Blue)
            : ("Conectando", Theme.Blue);
        if (pill.Text != state)
        {
            pill.Text = state;
            pill.Tone = tone;
            var s = DeviceDpi / 96f;
            pill.Width = pill.PreferredWidth;
            pill.Left = pill.Parent!.ClientSize.Width - pill.Width - (int)(16 * s);
        }

        var t = TimeSpan.FromSeconds(Math.Max(0, sessionSeconds));
        var timeText = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        if (lblTime.Text != timeText) lblTime.Text = timeText;
        lblTime.ForeColor = steam.IsFarming ? Theme.Text : Theme.Mix(Theme.Text, Theme.Surface, 0.45f);
        RefreshTotals();

        if (cfg.MetaHoras > 0)
        {
            var done = cfg.TotalAtual - cfg.MetaInicioSegundos;
            var target = cfg.MetaHoras * 3600;
            barGoal.Value = done / target;
            lblGoal.Text = $"+{cfg.MetaHoras:0.#} h · {100 * done / target:0.0}% · faltam {Fmt(target - done)}";
        }
        else
        {
            barGoal.Value = 0;
            lblGoal.Text = "Sem meta: roda até você clicar em Parar.";
        }

        var trayText = running ? (steam.IsFarming ? $"{AppTitle}: contando" : $"{AppTitle}: aguardando") : $"{AppTitle}: parado";
        if (tray.Text != trayText) tray.Text = trayText;
    }

    void RefreshTotals()
    {
        var text = "Total neste jogo pelo Qhours: " + Fmt(cfg.TotalAtual);
        if (lblTotal.Text != text) lblTotal.Text = text;
    }

    static string Fmt(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, s));
        return $"{(int)t.TotalHours}h {t.Minutes:00}min";
    }

    // ---------- Log / utilidades ----------

    public void PostLog(string msg) => PostLog(msg, null);

    void PostLog(string msg, Color? tone) => BeginInvokeSafe(() =>
    {
        var now = DateTime.Now;
        var color = tone ?? Classify(msg);
        log.BeginUpdate();
        log.Items.Add(new LogEntry(now, msg, color));
        while (log.Items.Count > 300) log.Items.RemoveAt(0);
        log.EndUpdate();
        log.TopIndex = log.Items.Count - 1;
        storage.AppendLog($"{now:dd/MM HH:mm:ss}  {msg}");
    });

    static Color Classify(string msg) =>
        msg.Contains("Contando horas") ? Theme.Green
        : msg.StartsWith("Falha") || msg.Contains("incorret") || msg.Contains("expirou") || msg.Contains("Erro") ? Theme.Red
        : msg.Contains("Pausado") || msg.Contains("jogando em outro") || msg.Contains("esperar") || msg.Contains("perdida") ? Theme.Orange
        : Theme.Text;

    void BeginInvokeSafe(Action a)
    {
        if (IsDisposed || Disposing) return;
        if (!IsHandleCreated) { a(); return; }
        try { BeginInvoke(a); } catch (ObjectDisposedException) { } catch (InvalidOperationException) { }
    }

    void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (steam.IsRunning && !exiting && e.CloseReason == CloseReason.UserClosing)
        {
            if (!Dialogs.Confirm(this, "Sair do Qhours",
                    "Parar a contagem de horas e sair?\n\nPara continuar em segundo plano, minimize: o Qhours vai para a bandeja.",
                    "Parar e sair", "Continuar"))
            {
                e.Cancel = true;
                return;
            }
        }
        timer.Stop();
        StopSteam();
        tray.Visible = false;
        tray.Dispose();
    }
}
