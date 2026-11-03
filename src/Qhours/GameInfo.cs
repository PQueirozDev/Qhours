using System.Text.Json;

namespace Qhours;

/// <summary>Nome e capa do jogo pela API pública da loja Steam (sem login). A capa fica em cache no disco.</summary>
sealed class GameInfoService(string cacheDir)
{
    static readonly HttpClient Http = CreateClient();

    public sealed record Info(string? Name, Image? Cover, bool FoundInStore);

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Qhours/2.0 (+https://github.com/PQueirozDev/Qhours)");
        return c;
    }

    public async Task<Info> GetAsync(uint appId)
    {
        string? name = null, headerUrl = null;
        var found = false;
        try
        {
            var json = await Http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic&l=brazilian");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(appId.ToString(), out var e) &&
                e.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True &&
                e.TryGetProperty("data", out var d))
            {
                found = true;
                if (d.TryGetProperty("name", out var n)) name = n.GetString();
                if (d.TryGetProperty("header_image", out var h)) headerUrl = h.GetString();
            }
        }
        catch { /* sem internet ou loja fora: segue com o cache */ }

        return new Info(name, await LoadCoverAsync(appId, headerUrl), found);
    }

    async Task<Image?> LoadCoverAsync(uint appId, string? headerUrl)
    {
        var file = Path.Combine(cacheDir, $"capa_{appId}.jpg");
        if (!File.Exists(file))
        {
            var urls = new List<string>();
            if (!string.IsNullOrEmpty(headerUrl) && headerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) urls.Add(headerUrl);
            urls.Add($"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg");
            urls.Add($"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg");
            foreach (var url in urls)
            {
                try
                {
                    var bytes = await Http.GetByteArrayAsync(url);
                    if (bytes.Length < 1024 || bytes.Length > 5_000_000) continue;
                    await File.WriteAllBytesAsync(file, bytes);
                    break;
                }
                catch { }
            }
        }

        try
        {
            if (!File.Exists(file)) return null;
            using var ms = new MemoryStream(await File.ReadAllBytesAsync(file));
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch
        {
            try { File.Delete(file); } catch { }
            return null;
        }
    }
}
