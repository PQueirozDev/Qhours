using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qhours;

/// <summary>Arquivos locais. Token e dados do Steam Guard são criptografados com DPAPI (só este usuário do Windows,
/// neste PC). A senha da Steam nunca é gravada.
/// Local: %LOCALAPPDATA%\Qhours, ou a pasta "dados" ao lado do .exe se ela existir (modo portátil).</summary>
public sealed class Storage
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Qhours-v1-DPAPI");
    static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("RDR2Horas-v1-DPAPI");
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string Dir { get; }
    public string CacheDir { get; }
    string AccountFile => Path.Combine(Dir, "conta.json");
    string TokenFile => Path.Combine(Dir, "token.bin");
    string GuardFile => Path.Combine(Dir, "guard.bin");
    string SettingsFile => Path.Combine(Dir, "config.json");
    string LegacyProgressFile => Path.Combine(Dir, "progresso.json");
    string LogFile => Path.Combine(Dir, "log.txt");

    public Storage()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "dados");
        Dir = Directory.Exists(portable)
            ? portable
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qhours");
        CacheDir = Path.Combine(Dir, "cache");
        Directory.CreateDirectory(CacheDir);
    }

    public sealed class Account
    {
        public string Username { get; set; } = "";
        public uint LoginId { get; set; }
    }

    public sealed class Settings
    {
        public const uint DefaultAppId = 1174180;
        public uint AppId { get; set; } = DefaultAppId;
        public string? AppName { get; set; } = "Red Dead Redemption 2";
        /// <summary>Segundos acumulados por este programa, por AppID.</summary>
        public Dictionary<uint, double> Totais { get; set; } = new();
        public double MetaHoras { get; set; }
        public double MetaInicioSegundos { get; set; }

        [JsonIgnore]
        public double TotalAtual
        {
            get => Totais.TryGetValue(AppId, out var v) ? v : 0;
            set => Totais[AppId] = value;
        }
    }

    sealed class LegacyProgress
    {
        public double TotalSegundos { get; set; }
        public double MetaHoras { get; set; }
        public double MetaInicioSegundos { get; set; }
    }

    public Account? LoadAccount() => ReadJson<Account>(AccountFile);

    public Account SaveAccount(string username)
    {
        var acc = LoadAccount();
        if (acc == null || !string.Equals(acc.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            // LoginID próprio e fixo: evita colidir com a sessão do seu cliente Steam neste mesmo PC
            acc = new Account { Username = username, LoginId = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue) };
        }
        acc.Username = username;
        WriteJson(AccountFile, acc);
        return acc;
    }

    public string? LoadToken() => ReadSecret(TokenFile);
    public void SaveToken(string token) => WriteSecret(TokenFile, token);
    public void DeleteToken() => TryDelete(TokenFile);

    public string? LoadGuardData() => ReadSecret(GuardFile);
    public void SaveGuardData(string data) => WriteSecret(GuardFile, data);

    public void ForgetAccount()
    {
        TryDelete(TokenFile);
        TryDelete(GuardFile);
        TryDelete(AccountFile);
    }

    public Settings LoadSettings()
    {
        var s = ReadJson<Settings>(SettingsFile);
        if (s != null) return s;

        s = new Settings();
        // Migra o progresso da versão anterior (RDR2 Horas)
        if (ReadJson<LegacyProgress>(LegacyProgressFile) is { } old)
        {
            s.Totais[Settings.DefaultAppId] = old.TotalSegundos;
            s.MetaHoras = old.MetaHoras;
            s.MetaInicioSegundos = old.MetaInicioSegundos;
        }
        return s;
    }

    public void SaveSettings(Settings s)
    {
        try { WriteJson(SettingsFile, s); } catch { /* disco cheio/bloqueado: tenta de novo no próximo salvamento */ }
    }

    public void AppendLog(string line)
    {
        try
        {
            var fi = new FileInfo(LogFile);
            if (fi.Exists && fi.Length > 1_000_000) fi.Delete();
            File.AppendAllText(LogFile, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* log é opcional */ }
    }

    static string? ReadSecret(string file)
    {
        if (!File.Exists(file)) return null;
        byte[] data;
        try { data = File.ReadAllBytes(file); } catch { return null; }
        foreach (var entropy in new[] { Entropy, LegacyEntropy })
        {
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(data, entropy, DataProtectionScope.CurrentUser)); }
            catch (CryptographicException) { }
        }
        return null;
    }

    static void WriteSecret(string file, string value)
    {
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        AtomicWrite(file, enc);
    }

    static T? ReadJson<T>(string file) where T : class
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file)) : null; }
        catch { return null; }
    }

    static void WriteJson<T>(string file, T value) =>
        AtomicWrite(file, JsonSerializer.SerializeToUtf8Bytes(value, JsonOpts));

    static void AtomicWrite(string file, byte[] data)
    {
        var tmp = file + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, file, overwrite: true);
    }

    static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
    }
}
