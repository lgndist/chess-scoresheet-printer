using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using NotasyonOtomasyonu.App.Update;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Uygulama içi güncelleme: sürüm bilgisi, indirme + doğrulama, kurulum ve geri alma.</summary>
[Collection("updater")] // ortam değişkeni ve geçici klasör paylaşılıyor: sırayla
public class UpdaterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csp-updater-test-" + Guid.NewGuid().ToString("N"));

    public UpdaterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CSP_UPDATE_API", null);
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Theory]
    [InlineData("v1.6.0", "1.6.0")]
    [InlineData("1.6", "1.6.0")]
    [InlineData("V1.10.3", "1.10.3")]
    [InlineData("v2.0.0-beta.1", "2.0.0")]
    [InlineData("v1.6.0+abc", "1.6.0")]
    public void ParseVersion_Tags(string tag, string expected) => Assert.Equal(Version.Parse(expected), Updater.ParseVersion(tag));

    [Theory]
    [InlineData("")]
    [InlineData("sürüm")]
    [InlineData(null)]
    public void ParseVersion_Invalid(string? tag) => Assert.Null(Updater.ParseVersion(tag));

    [Fact]
    public void Versions_CompareNumerically() => Assert.True(Updater.ParseVersion("v1.10.0") > Updater.ParseVersion("v1.9.9"));

    private static string ReleaseJson(string tag, bool zip = true, bool sha = true, string baseUrl = "https://example.invalid") =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag, body = "### Değişenler\n- **Yeni** özellik", html_url = baseUrl + "/rel", published_at = "2026-10-23T10:00:00Z",
            assets = new object[]
            {
                zip ? new { name = $"ChessScoresheetPrinter-{tag}-win-x64.zip", browser_download_url = baseUrl + "/p.zip", size = 1234L } : null!,
                sha ? new { name = $"ChessScoresheetPrinter-{tag}-win-x64.sha256", browser_download_url = baseUrl + "/p.sha256", size = 90L } : null!,
                new { name = "kaynak.tar.gz", browser_download_url = baseUrl + "/src", size = 1L }
            }.Where(a => a is not null).ToArray()
        });

    [Fact]
    public void Parse_ReadsZipShaAndNotes()
    {
        using var doc = JsonDocument.Parse(ReleaseJson("v1.7.0"));
        var r = Updater.Parse(doc.RootElement)!;
        Assert.Equal(new Version(1, 7, 0), r.Version);
        Assert.EndsWith("p.zip", r.ZipUrl);
        Assert.EndsWith("p.sha256", r.ShaUrl);
        Assert.Equal(1234, r.ZipSize);
        Assert.Contains("Değişenler", r.Notes);
        Assert.NotNull(r.Published);
    }

    [Fact]
    public void Parse_WithoutWindowsPackage_IsNull()
    {
        using var doc = JsonDocument.Parse(ReleaseJson("v1.7.0", zip: false));
        Assert.Null(Updater.Parse(doc.RootElement));
    }

    [Fact]
    public void Parse_BadTag_IsNull()
    {
        using var doc = JsonDocument.Parse(ReleaseJson("en-yeni"));
        Assert.Null(Updater.Parse(doc.RootElement));
    }

    /// <summary>İçinde exe (ve yazı tipi) olan paket; isteğe bağlı olarak ayarları ezmeye çalışan config.json.</summary>
    private byte[] Package(string exeContent, bool withExe = true, bool withConfig = false)
    {
        var src = Path.Combine(_dir, "pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(src, "LatoFont"));
        if (withExe) File.WriteAllText(Path.Combine(src, "ChessScoresheetPrinter.exe"), exeContent);
        File.WriteAllText(Path.Combine(src, "LatoFont", "Lato.ttf"), "font-" + exeContent);
        if (withConfig) File.WriteAllText(Path.Combine(src, "config.json"), "{\"ezilmemeli\":true}");
        var zip = src + ".zip";
        ZipFile.CreateFromDirectory(src, zip);
        return File.ReadAllBytes(zip);
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static LocalServer Serve(byte[] zip, string sha, string tag = "v9.9.9")
    {
        LocalServer? s = null;
        s = new LocalServer(req => req.Url!.AbsolutePath switch
        {
            "/latest.json" => LocalServer.Text(ReleaseJson(tag, baseUrl: s!.BaseUrl)),
            "/p.zip" => (200, zip),
            "/p.sha256" => LocalServer.Text($"{sha}  ChessScoresheetPrinter-{tag}-win-x64.zip\n"),
            _ => LocalServer.Text("yok", 404)
        });
        return s;
    }

    [Fact]
    public async Task Latest_And_Download_VerifiesChecksum()
    {
        var zip = Package("YENI");
        using var server = Serve(zip, Sha(zip));
        Environment.SetEnvironmentVariable("CSP_UPDATE_API", server.BaseUrl + "/latest.json");

        var r = await Updater.LatestAsync();
        Assert.NotNull(r);
        Assert.Equal(new Version(9, 9, 9), r!.Version);
        Assert.True(Updater.IsNewer(r));

        var reports = new List<(long, long)>();
        var dir = await Updater.DownloadAsync(r, new SyncProgress(reports));
        Assert.True(File.Exists(Path.Combine(dir, "ChessScoresheetPrinter.exe")));
        Assert.Equal(zip.Length, reports.Last().Item1);
    }

    [Fact]
    public async Task Download_CorruptPackage_IsRejected()
    {
        var zip = Package("YENI");
        using var server = Serve(zip, new string('0', 64)); // yanlış özet
        Environment.SetEnvironmentVariable("CSP_UPDATE_API", server.BaseUrl + "/latest.json");
        var r = (await Updater.LatestAsync())!;
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Updater.DownloadAsync(r, null));
        Assert.Contains("SHA-256", ex.Message);
    }

    [Fact]
    public async Task Download_PackageWithoutExe_IsRejected()
    {
        var zip = Package("YENI", withExe: false);
        using var server = Serve(zip, Sha(zip));
        Environment.SetEnvironmentVariable("CSP_UPDATE_API", server.BaseUrl + "/latest.json");
        var r = (await Updater.LatestAsync())!;
        await Assert.ThrowsAsync<InvalidDataException>(() => Updater.DownloadAsync(r, null));
    }

    [Fact]
    public async Task Download_ConnectionDropsMidway_Throws()
    {
        var zip = Package("YENI");
        using var listener = new System.Net.HttpListener();
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start(); int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var baseUrl = $"http://localhost:{port}";
        listener.Prefixes.Add(baseUrl + "/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                if (ctx.Request.Url!.AbsolutePath == "/latest.json")
                {
                    var b = System.Text.Encoding.UTF8.GetBytes(ReleaseJson("v9.9.9", baseUrl: baseUrl));
                    ctx.Response.OutputStream.Write(b); ctx.Response.Close();
                    continue;
                }
                // Paketin yarısını gönderip bağlantıyı kopar
                ctx.Response.ContentLength64 = zip.Length;
                try { ctx.Response.OutputStream.Write(zip, 0, zip.Length / 2); ctx.Response.Abort(); } catch { }
            }
        });
        Environment.SetEnvironmentVariable("CSP_UPDATE_API", baseUrl + "/latest.json");
        var r = (await Updater.LatestAsync())!;
        await Assert.ThrowsAnyAsync<Exception>(() => Updater.DownloadAsync(r, null));
        listener.Stop();
    }

    [Fact]
    public async Task Latest_ServerDown_Throws()
    {
        Environment.SetEnvironmentVariable("CSP_UPDATE_API", "http://localhost:1/latest.json");
        await Assert.ThrowsAnyAsync<Exception>(() => Updater.LatestAsync());
    }

    /// <summary>Kurulacak "program klasörü": eski exe, kullanıcının ayarları ve şablonu.</summary>
    private (string App, string Exe) InstalledApp()
    {
        var app = Path.Combine(_dir, "program");
        Directory.CreateDirectory(Path.Combine(app, "templates"));
        var exe = Path.Combine(app, "ChessScoresheetPrinter.exe");
        File.WriteAllText(exe, "ESKI");
        File.WriteAllText(Path.Combine(app, "config.json"), "{\"benim\":\"ayarlarim\"}");
        File.WriteAllText(Path.Combine(app, "templates", "sablon.json"), "kullanici");
        return (app, exe);
    }

    private string Extract(byte[] zip)
    {
        var d = Path.Combine(_dir, "acik-" + Guid.NewGuid().ToString("N"));
        var z = d + ".zip";
        File.WriteAllBytes(z, zip);
        ZipFile.ExtractToDirectory(z, d);
        return d;
    }

    [Fact]
    public void Install_ReplacesExe_KeepsSettingsAndTemplates()
    {
        var (app, exe) = InstalledApp();
        Updater.Install(Extract(Package("YENI", withConfig: true)), exe, restart: false);
        Assert.Equal("YENI", File.ReadAllText(exe));
        Assert.Equal("ESKI", File.ReadAllText(exe + ".old"));                                    // geri dönüş için
        Assert.Equal("{\"benim\":\"ayarlarim\"}", File.ReadAllText(Path.Combine(app, "config.json"))); // pakettekine rağmen ezilmedi
        Assert.Equal("kullanici", File.ReadAllText(Path.Combine(app, "templates", "sablon.json")));
        Assert.Equal("font-YENI", File.ReadAllText(Path.Combine(app, "LatoFont", "Lato.ttf")));
    }

    [Fact]
    public void Install_TwiceInARow_Works()
    {
        var (_, exe) = InstalledApp();
        Updater.Install(Extract(Package("V2")), exe, restart: false);
        Updater.Install(Extract(Package("V3")), exe, restart: false); // önceki .old silinip yenisi yazılır
        Assert.Equal("V3", File.ReadAllText(exe));
        Assert.Equal("V2", File.ReadAllText(exe + ".old"));
    }

    [Fact]
    public void Install_Failure_RollsBack()
    {
        var (app, exe) = InstalledApp();
        // Paketteki "templates" bir DOSYA; programda aynı adlı klasör var → kopyalama hata verir.
        var src = Path.Combine(_dir, "bozuk");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "ChessScoresheetPrinter.exe"), "YENI");
        File.WriteAllText(Path.Combine(src, "templates"), "dosya");
        Assert.ThrowsAny<Exception>(() => Updater.Install(src, exe, restart: false));
        Assert.Equal("ESKI", File.ReadAllText(exe));          // eski exe geri kondu
        Assert.False(File.Exists(exe + ".old"));
        Assert.Equal("kullanici", File.ReadAllText(Path.Combine(app, "templates", "sablon.json")));
    }

    [Fact]
    public void Install_WhileTargetIsRunning_RenamesRunningExe()
    {
        // Çalışan bir exe'nin üzerine kurulum: Windows çalışan dosyanın adını değiştirmeye izin verir.
        var (_, exe) = InstalledApp();
        var cmd = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        File.Copy(cmd, exe, overwrite: true);
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            Thread.Sleep(500);
            Assert.False(p.HasExited);
            Updater.Install(Extract(Package("YENI")), exe, restart: false);
            Assert.Equal("YENI", File.ReadAllText(exe));
            Assert.True(File.Exists(exe + ".old"));
        }
        finally { try { p.Kill(); p.WaitForExit(5000); } catch { } }
    }

    /// <summary>Progress&lt;T&gt; raporları eşzamanlı topla (test sonunda eksiksiz olsun).</summary>
    private sealed class SyncProgress(List<(long, long)> list) : IProgress<(long Done, long Total)>
    {
        public void Report((long Done, long Total) v) { lock (list) list.Add(v); }
    }
}
