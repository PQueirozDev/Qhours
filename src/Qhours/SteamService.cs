using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace Qhours;

/// <summary>Sessão Steam mínima: login, informa "jogando AppID X", trata bloqueio/reconexão.
/// Não abre o jogo e não define status de amigos (fica Offline, as horas contam mesmo assim).</summary>
public sealed class SteamService
{
    static readonly TimeSpan DelayAfterBlock = TimeSpan.FromSeconds(60);
    static readonly TimeSpan DelayLoggedInElsewhere = TimeSpan.FromMinutes(5);
    static readonly TimeSpan DelayRateLimit = TimeSpan.FromMinutes(30);
    static readonly TimeSpan HealthySession = TimeSpan.FromMinutes(10);

    readonly Storage storage;
    readonly IAuthenticator authenticator;
    readonly object gate = new();

    // Um cliente novo por execução: nada de callbacks antigos vazando para a próxima.
    SteamClient? client;
    CallbackManager? manager;
    SteamUser? user;
    Thread? thread;
    CancellationTokenSource? cts;
    int generation;

    volatile bool running;
    volatile bool stopping;
    volatile bool loggedOn;
    volatile bool playSent;
    volatile bool playingBlocked;
    bool authInProgress;
    string? pendingUsername;
    string? pendingPassword;   // só em memória, apagada logo após o login
    DateTime? reconnectAt;
    DateTime? replayAt;
    DateTime? loggedOnAt;
    TimeSpan? nextDelay;
    int failures;

    /// <summary>Jogo informado à Steam. Só pode mudar com o serviço parado.</summary>
    public uint AppId { get; set; } = Storage.Settings.DefaultAppId;
    public string GameName { get; set; } = "Red Dead Redemption 2";

    public event Action<string>? Log;
    public event Action? StateChanged;
    /// <summary>Erro que exige ação do usuário; a interface deve chamar Stop().</summary>
    public event Action<string>? Fatal;

    public bool IsRunning => running;
    public bool IsLoggedOn => loggedOn;
    public bool IsBlocked => playingBlocked;
    public bool IsFarming => running && !stopping && loggedOn && playSent && !playingBlocked;

    public SteamService(Storage storage, IAuthenticator authenticator)
    {
        this.storage = storage;
        this.authenticator = authenticator;
    }

    /// <summary>Inicia. Sem token salvo, exige usuário e senha.</summary>
    public void Start(string? username, string? password)
    {
        if (running) return;

        int gen;
        lock (gate)
        {
            gen = ++generation;
            cts = new CancellationTokenSource();
            client = new SteamClient();
            manager = new CallbackManager(client);
            user = client.GetHandler<SteamUser>()!;
            manager.Subscribe<SteamClient.ConnectedCallback>(cb => OnConnected(gen));
            manager.Subscribe<SteamClient.DisconnectedCallback>(cb => OnDisconnected(gen));
            manager.Subscribe<SteamUser.LoggedOnCallback>(cb => OnLoggedOn(gen, cb));
            manager.Subscribe<SteamUser.LoggedOffCallback>(cb => OnLoggedOff(gen, cb));
            manager.Subscribe<SteamUser.PlayingSessionStateCallback>(cb => OnPlayingSessionState(gen, cb));

            pendingUsername = username;
            pendingPassword = password;
            failures = 0;
            nextDelay = null;
            reconnectAt = null;
            replayAt = null;
            loggedOnAt = null;
            stopping = false;
            loggedOn = false;
            playSent = false;
            playingBlocked = false;
            authInProgress = false;
            running = true;
        }

        var c = client;
        var m = manager;
        thread = new Thread(() => Loop(gen, c, m)) { IsBackground = true, Name = "Steam", Priority = ThreadPriority.BelowNormal };
        thread.Start();
        Emit("Conectando à Steam...");
        c.Connect();
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (!running) return;
        SteamClient? c;
        lock (gate)
        {
            stopping = true;
            replayAt = null;
            reconnectAt = null;
            pendingPassword = null;
            generation++;          // invalida autenticações e callbacks em andamento
            cts?.Cancel();
            c = client;
        }

        try
        {
            if (c is { IsConnected: true } && loggedOn)
            {
                SendGamesCore(c, play: false);
                user?.LogOff();
                for (var i = 0; i < 30 && loggedOn; i++) Thread.Sleep(100);
            }
            // Desconecta com o laço ainda processando callbacks, para encerrar limpo
            c?.Disconnect();
            for (var i = 0; i < 20 && c is { IsConnected: true }; i++) Thread.Sleep(100);
        }
        catch { }

        running = false;
        thread?.Join(3000);
        lock (gate)
        {
            loggedOn = false;
            playSent = false;
            playingBlocked = false;
            cts?.Dispose();
            cts = null;
            client = null;
            manager = null;
            user = null;
        }
        Emit("Parado. A Steam deixa de contar horas.");
        StateChanged?.Invoke();
    }

    bool IsCurrent(int gen) => gen == Volatile.Read(ref generation) && running && !stopping;

    void Loop(int gen, SteamClient c, CallbackManager m)
    {
        while (running && gen == Volatile.Read(ref generation) || (running && stopping))
        {
            try
            {
                m.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                if (!IsCurrent(gen)) continue;
                var now = DateTime.UtcNow;
                if (reconnectAt is { } r && now >= r)
                {
                    reconnectAt = null;
                    Emit("Reconectando...");
                    c.Connect();
                }
                if (replayAt is { } p && now >= p)
                {
                    replayAt = null;
                    SendGames(gen, play: true);
                }
            }
            catch (Exception ex)
            {
                Emit("Erro interno: " + ex.Message);
            }
        }
    }

    async void OnConnected(int gen)
    {
        if (!IsCurrent(gen)) return;
        var token = storage.LoadToken();
        var account = storage.LoadAccount();

        if (token != null && account != null)
        {
            pendingPassword = null; // não precisa da senha: descarta já
            Emit("Conectado. Entrando com a sessão salva...");
            LogOnWithToken(account, token);
            return;
        }

        if (string.IsNullOrEmpty(pendingUsername) || string.IsNullOrEmpty(pendingPassword))
        {
            RaiseFatal(gen, "Nenhuma sessão salva: informe usuário e senha e clique em Iniciar.");
            return;
        }

        if (authInProgress) return;
        authInProgress = true;
        var c = client!;
        var ct = cts!.Token;
        try
        {
            Emit("Conectado. Autenticando (o Steam Guard pode pedir confirmação)...");
            var session = await c.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
            {
                Username = pendingUsername,
                Password = pendingPassword,
                IsPersistentSession = true,
                GuardData = storage.LoadGuardData(),
                Authenticator = authenticator,
                DeviceFriendlyName = "Qhours (" + Environment.MachineName + ")",
            });
            var poll = await session.PollingWaitForResultAsync(ct);

            lock (gate)
            {
                if (!IsCurrent(gen)) return; // usuário parou ou reiniciou durante o login: descarta
                pendingPassword = null;
                if (poll.NewGuardData != null) storage.SaveGuardData(poll.NewGuardData);
                storage.SaveToken(poll.RefreshToken);
                var acc = storage.SaveAccount(poll.AccountName);
                Emit("Autenticado. Sessão salva com criptografia do Windows (DPAPI).");
                LogOnWithToken(acc, poll.RefreshToken);
            }
        }
        catch (AuthenticationException ex)
        {
            if (!IsCurrent(gen)) return;
            pendingPassword = null;
            RaiseFatal(gen, ex.Result switch
            {
                EResult.InvalidPassword => "Usuário ou senha incorretos.",
                EResult.RateLimitExceeded or EResult.AccountLoginDeniedThrottle =>
                    "A Steam bloqueou temporariamente novas tentativas. Aguarde ~30 min antes de tentar de novo.",
                EResult.FileNotFound or EResult.Expired => "O pedido de login expirou. Tente novamente.",
                _ => "Falha na autenticação: " + ex.Result,
            });
        }
        catch (OperationCanceledException)
        {
            if (!IsCurrent(gen)) return;
            pendingPassword = null;
            RaiseFatal(gen, "Login cancelado.");
        }
        catch (Exception ex)
        {
            if (!IsCurrent(gen)) return;
            Emit("Falha durante a autenticação: " + ex.Message);
            nextDelay = Backoff();
            try { c.Disconnect(); } catch { }
        }
        finally
        {
            if (gen == Volatile.Read(ref generation)) authInProgress = false;
        }
    }

    void LogOnWithToken(Storage.Account acc, string token)
    {
        user?.LogOn(new SteamUser.LogOnDetails
        {
            Username = acc.Username,
            AccessToken = token,
            ShouldRememberPassword = true,
            LoginID = acc.LoginId,
        });
    }

    void OnLoggedOn(int gen, SteamUser.LoggedOnCallback cb)
    {
        if (!IsCurrent(gen)) return;
        if (cb.Result == EResult.OK)
        {
            loggedOn = true;
            loggedOnAt = DateTime.UtcNow;
            playingBlocked = false;
            Emit($"Login OK. Avisando a Steam: jogando {GameName} ({AppId}).");
            // Pequena espera para receber o estado da sessão de jogo antes de iniciar (mesma lógica do ASF)
            replayAt = DateTime.UtcNow.AddSeconds(2);
            StateChanged?.Invoke();
            return;
        }

        switch (cb.Result)
        {
            case EResult.InvalidPassword:
            case EResult.AccessDenied:
            case EResult.Expired:
            case EResult.Revoked:
            case EResult.InvalidSignature:
                storage.DeleteToken();
                RaiseFatal(gen, "A sessão salva expirou ou foi revogada (" + cb.Result + "). Entre de novo com sua senha.");
                return;
            case EResult.RateLimitExceeded:
            case EResult.AccountLoginDeniedThrottle:
                nextDelay = DelayRateLimit;
                Emit("A Steam pediu para esperar (limite de tentativas). Nova tentativa em 30 min.");
                return;
            default:
                Emit("Login recusado: " + cb.Result + " / " + cb.ExtendedResult + ". Tentando de novo mais tarde.");
                return;
        }
    }

    void OnLoggedOff(int gen, SteamUser.LoggedOffCallback cb)
    {
        loggedOn = false;
        playSent = false;
        if (!IsCurrent(gen)) return;
        if (cb.Result == EResult.LoggedInElsewhere)
        {
            nextDelay = DelayLoggedInElsewhere;
            Emit("A conta foi usada para jogar em outro lugar. Pausado; nova tentativa em 5 min.");
        }
        else
        {
            Emit("Desconectado pela Steam: " + cb.Result);
        }
        StateChanged?.Invoke();
    }

    void OnDisconnected(int gen)
    {
        loggedOn = false;
        playSent = false;
        StateChanged?.Invoke();
        if (!IsCurrent(gen)) return;

        // Só zera o recuo depois de uma sessão estável, para nunca martelar a Steam
        if (loggedOnAt is { } t && DateTime.UtcNow - t >= HealthySession) failures = 0;
        loggedOnAt = null;

        var delay = nextDelay ?? Backoff();
        nextDelay = null;
        reconnectAt = DateTime.UtcNow + delay;
        Emit($"Conexão perdida. Reconectando em {delay.TotalSeconds:0} s.");
    }

    TimeSpan Backoff()
    {
        var s = Math.Min(600, 30 * Math.Pow(2, Math.Min(failures, 5)));
        failures++;
        return TimeSpan.FromSeconds(s);
    }

    void OnPlayingSessionState(int gen, SteamUser.PlayingSessionStateCallback cb)
    {
        if (!IsCurrent(gen)) return;
        var wasBlocked = playingBlocked;
        playingBlocked = cb.PlayingBlocked;
        if (cb.PlayingBlocked && !wasBlocked)
        {
            replayAt = null;
            playSent = false; // só volta a contar depois de reenviar o "jogando"
            Emit($"Você está jogando em outro lugar (AppID {cb.PlayingAppID}). Contagem pausada até você terminar.");
        }
        else if (!cb.PlayingBlocked && wasBlocked)
        {
            Emit("Jogo em outro lugar encerrado. Retomando em 60 s.");
            replayAt = DateTime.UtcNow + DelayAfterBlock;
        }
        StateChanged?.Invoke();
    }

    void SendGames(int gen, bool play)
    {
        var c = client;
        if (!IsCurrent(gen) || c is not { IsConnected: true } || !loggedOn) return;
        if (play && playingBlocked) return;
        SendGamesCore(c, play);
        if (play) Emit($"Steam informada: jogando {GameName}. Contando horas.");
        StateChanged?.Invoke();
    }

    void SendGamesCore(SteamClient c, bool play)
    {
        var msg = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedWithDataBlob);
        msg.Body.client_os_type = unchecked((uint)EOSType.Windows10);
        if (play)
        {
            msg.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = new GameID(AppId) });
        }
        c.Send(msg);
        playSent = play;
    }

    void RaiseFatal(int gen, string message)
    {
        if (!IsCurrent(gen)) return;
        Emit(message);
        Fatal?.Invoke(message);
    }

    void Emit(string message) => Log?.Invoke(message);
}
