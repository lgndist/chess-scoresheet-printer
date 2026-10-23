using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace NotasyonOtomasyonu.App.Update;

/// <summary>GitHub'daki bir sürüm: numarası, notları ve Windows paketi.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Notes, string HtmlUrl, DateTime? Published,
                                 string ZipUrl, string ZipName, long ZipSize, string? ShaUrl);

/// <summary>
/// Uygulama içinden güncelleme. Son sürüm GitHub'dan sorulur (herkese açık sürüm listesi; kimlik
/// bilgisi gönderilmez); yeniyse Windows ZIP paketi indirilir, SHA-256 özeti sürümdeki .sha256
/// dosyasıyla doğrulanır ve paket geçici klasöre açılır. Uygulanırken çalışan exe yeniden adlandırılır
/// (Windows çalışan dosyanın adını değiştirmeye izin verir, silmeye değil), yenisi yerine kopyalanır ve
/// program yeni sürümle yeniden başlar; eski dosya sonraki açılışta silinir. Pakette yalnız exe ve yazı
/// tipleri vardır: exe yanındaki config.json, şablonlar ve çıktılar olduğu gibi kalır.
/// </summary>
public static class Updater
{
    public const string Repo = "lgndist/chess-scoresheet-printer";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases";
    private const string OldSuffix = ".old";

    /// <summary>
    /// Sürüm bilgisinin adresi. CSP_UPDATE_API ortam değişkeniyle değiştirilebilir (yerel deneme sunucusu
    /// ya da ayna); yoksa GitHub'daki son sürüm.
    /// </summary>
    public static string ApiUrl => Environment.GetEnvironmentVariable("CSP_UPDATE_API") is { Length: > 0 } u
        ? u : $"https://api.github.com/repos/{Repo}/releases/latest";

    /// <summary>Çalışan sürüm (üç haneli).</summary>
    public static Version Current
    {
        get
        {
            var v = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    private static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ChessScoresheetPrinter", Current.ToString()));
        return http;
    }

    /// <summary>GitHub'daki son yayımlanmış sürüm (taslak/ön sürüm değil). Windows paketi yoksa null.</summary>
    public static async Task<ReleaseInfo?> LatestAsync(CancellationToken ct = default)
    {
        using var http = NewClient(TimeSpan.FromSeconds(20));
        using var req = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return Parse(doc.RootElement);
    }

    /// <summary>GitHub sürüm JSON'undan bilgi (test edilebilsin diye ayrı).</summary>
    public static ReleaseInfo? Parse(JsonElement r)
    {
        var tag = r.GetProperty("tag_name").GetString() ?? "";
        if (ParseVersion(tag) is not { } version) return null;
        string? zipUrl = null, zipName = null, shaUrl = null;
        long size = 0;
        foreach (var a in r.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString() ?? "";
            if (name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)) { zipUrl = url; zipName = name; size = a.GetProperty("size").GetInt64(); }
            else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) shaUrl = url;
        }
        if (zipUrl is null) return null;
        DateTime? published = r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String && p.TryGetDateTime(out var d) ? d.ToLocalTime() : null;
        return new ReleaseInfo(version, tag, r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                               r.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage,
                               published, zipUrl, zipName!, size, shaUrl);
    }

    /// <summary>"v1.6.0" → 1.6.0; tanınmazsa null.</summary>
    public static Version? ParseVersion(string? tag)
    {
        var s = (tag ?? "").Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut > 0) s = s[..cut];
        if (!Version.TryParse(s, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public static bool IsNewer(ReleaseInfo r) => r.Version > Current;

    /// <summary>
    /// Paketi indirir, özetini doğrular ve geçici klasöre açar; açılan klasörü döndürür.
    /// <paramref name="progress"/>: (indirilen, toplam) bayt.
    /// </summary>
    public static async Task<string> DownloadAsync(ReleaseInfo r, IProgress<(long Done, long Total)>? progress, CancellationToken ct = default)
    {
        var work = Path.Combine(Path.GetTempPath(), "ChessScoresheetPrinter-update-" + r.Version);
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        var zip = Path.Combine(work, r.ZipName);

        using var http = NewClient(TimeSpan.FromMinutes(15));
        using (var resp = await http.GetAsync(r.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? r.ZipSize;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(zip);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                done += n;
                progress?.Report((done, total));
            }
        }

        if (r.ShaUrl is not null)
        {
            var expected = (await http.GetStringAsync(r.ShaUrl, ct).ConfigureAwait(false)).Trim().Split(' ', '\t')[0].ToLowerInvariant();
            string actual;
            await using (var fs = File.OpenRead(zip)) actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false)).ToLowerInvariant();
            if (expected.Length == 64 && expected != actual)
                throw new InvalidDataException("İndirilen paketin özeti (SHA-256) sürümdekiyle tutmuyor; paket bozuk indirilmiş olabilir. Lütfen yeniden deneyin.");
        }

        var extract = Path.Combine(work, "paket");
        ZipFile.ExtractToDirectory(zip, extract);
        if (FindExe(extract) is null)
            throw new InvalidDataException("Pakette ChessScoresheetPrinter.exe bulunamadı.");
        return extract;
    }

    private static string? FindExe(string dir)
        => Directory.EnumerateFiles(dir, "ChessScoresheetPrinter.exe", SearchOption.AllDirectories).FirstOrDefault();

    /// <summary>
    /// Açılmış paketi programın klasörüne kurar ve yeni sürümü başlatır (çağıran ardından programı kapatır).
    /// Hata olursa eski exe geri konur ve hata fırlatılır.
    /// </summary>
    public static void Install(string extractDir, string? targetExe = null, bool restart = true)
    {
        var exe = targetExe ?? Environment.ProcessPath ?? throw new InvalidOperationException("Programın yolu bulunamadı.");
        var appDir = Path.GetDirectoryName(exe)!;
        var newExe = FindExe(extractDir)!;
        var root = Path.GetDirectoryName(newExe)!;   // paket kökü (exe'nin bulunduğu klasör)
        var old = exe + OldSuffix;

        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);                         // çalışan exe'nin adı değişir; dosya kilidi sürer
        try
        {
            File.Copy(newExe, exe);
            // Paketteki diğer dosyalar (yazı tipleri vb.) üzerine yazılır; pakette olmayanlara dokunulmaz.
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(f, newExe, StringComparison.OrdinalIgnoreCase)) continue;
                var rel = Path.GetRelativePath(root, f);
                if (rel.Equals("config.json", StringComparison.OrdinalIgnoreCase)) continue; // ayarlar asla ezilmez
                var dest = Path.Combine(appDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, overwrite: true);
            }
        }
        catch
        {
            try { if (File.Exists(exe)) File.Delete(exe); File.Move(old, exe); } catch { /* geri alma da olmadıysa eski dosya .old adıyla durur */ }
            throw;
        }
        if (!restart) return;
        // Yeni sürüm bu sürecin kapanmasını bekler (config.json'u ikisi aynı anda yazmasın), sonra eski exe'yi siler.
        Process.Start(new ProcessStartInfo(exe, $"--updated {Current} --wait {Environment.ProcessId}") { UseShellExecute = false, WorkingDirectory = appDir });
    }

    /// <summary>Program klasörüne yazılabiliyor mu (Program Files gibi korumalı klasörde değilse).</summary>
    public static bool CanWriteAppDir()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)!;
            var probe = Path.Combine(dir, ".update-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Önceki güncellemeden kalanları siler (açılışta): eski exe ve geçici klasördeki indirilmiş paket.
    /// Eski süreç henüz kapanmadıysa sonraki açılışa kalır.
    /// </summary>
    public static void CleanupOld()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null && File.Exists(exe + OldSuffix)) File.Delete(exe + OldSuffix);
        }
        catch { /* sonra */ }
        try
        {
            foreach (var d in Directory.EnumerateDirectories(Path.GetTempPath(), "ChessScoresheetPrinter-update-*"))
                try { Directory.Delete(d, recursive: true); } catch { /* kullanımda */ }
        }
        catch { /* sonra */ }
    }
}
