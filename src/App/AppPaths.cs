using System.Runtime.InteropServices;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Kullanıcı verisinin yeri. Varsayılan %APPDATA%\ChessScoresheetPrinter: config.json (ayarlar ve
/// şablonlar) ile kullanıcının eklediği dosyalar (cards: afiş, badges: logo ve fotoğraflar, reports:
/// rapor şablonları, templates: notasyon kağıdı görselleri). Güncellemede, exe başka klasöre taşınınca
/// ya da yeni sürüm ayrı klasöre kurulunca hiçbiri kaybolmaz.
/// Exe yanında "portable" adlı dosya varsa (USB bellekle taşınabilir kullanım) exe klasörü; CSP_CONFIG_DIR
/// ortam değişkeni verilmişse o klasör (testler) kullanılır. AppData'da henüz ayar yoksa exe yanındaki eski
/// config.json oraya bir kez kopyalanır (eskisi yedek olarak yerinde kalır); eski dosyalar
/// <see cref="MigrateAssets"/> ile taşınır.
/// </summary>
internal static class AppPaths
{
    public const string FolderName = "ChessScoresheetPrinter";
    private static readonly string[] AssetFolders = { "cards", "badges", "reports", "templates" };

    /// <summary>Kullanıcı verisi klasörü (config.json ve eklenen dosyalar).</summary>
    public static string DataDir { get; private set; } = AppContext.BaseDirectory;

    /// <summary>Ayar dosyası.</summary>
    public static string ConfigFile { get; private set; } = Path.Combine(AppContext.BaseDirectory, "config.json");

    /// <summary>Bu açılışta exe yanından taşındıysa eski config.json'un yolu.</summary>
    public static string? MigratedFrom { get; private set; }

    /// <summary>Veri klasörünü belirler (program başında bir kez).</summary>
    public static void Init(string baseDir)
    {
        var local = Path.Combine(baseDir, "config.json");
        DataDir = baseDir;
        ConfigFile = local;
        var env = Environment.GetEnvironmentVariable("CSP_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            try { Directory.CreateDirectory(env); DataDir = env; ConfigFile = Path.Combine(env, "config.json"); return; }
            catch { /* geçersiz klasör: varsayılana düş */ }
        }
        if (File.Exists(Path.Combine(baseDir, "portable"))) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "config.json");
            if (!File.Exists(path) && File.Exists(local))
            {
                File.Copy(local, path);
                MigratedFrom = local;
            }
            DataDir = dir;
            ConfigFile = path;
        }
        catch { /* AppData yazılamıyorsa eskisi gibi exe yanı */ }
    }

    /// <summary>
    /// Ayarlardaki dosya yollarını veri klasörüne taşır: exe klasöründeki afiş, logo, fotoğraf, rapor
    /// şablonu ve kağıt görselleri kopyalanır; başka yerden seçilmiş kağıt görselleri de templates'e
    /// kopyalanır (asıl dosya silinse ya da taşınsa da şablon bozulmasın). Yolu kırık bir dosya veri
    /// klasöründe aynı adla varsa ona bağlanır. Değişen yol sayısını döndürür.
    /// </summary>
    public static int MigrateAssets(AppConfig cfg, string baseDir)
    {
        int changed = 0;
        bool separate = !SameDir(baseDir, DataDir);
        if (separate)
            foreach (var f in AssetFolders)
                if (f != "templates") CopyTree(Path.Combine(baseDir, f), Path.Combine(DataDir, f));   // rapor şablonları adla tutulur

        string? Fix(string? path, string folder)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            string? result = null;
            if (separate && Under(baseDir, path) is { } rel && AssetFolders.Contains(rel.Split(Path.DirectorySeparatorChar)[0], StringComparer.OrdinalIgnoreCase))
            {
                var dst = Path.Combine(DataDir, rel);
                if (!File.Exists(dst) && File.Exists(path)) TryCopy(path, dst);
                if (File.Exists(dst)) result = dst;
            }
            else if (!File.Exists(path))
            {
                // Taşınmış/silinmiş eski klasör: veri klasöründe aynı adla duruyorsa ona bağla
                var cand = Path.Combine(DataDir, folder, Path.GetFileName(path));
                if (File.Exists(cand)) result = cand;
            }
            if (result is null || string.Equals(result, path, StringComparison.OrdinalIgnoreCase)) return path;
            changed++;
            return result;
        }

        cfg.Cards.LogoPath = Fix(cfg.Cards.LogoPath, "cards");
        cfg.Cards.LogoSourcePath = Fix(cfg.Cards.LogoSourcePath, "cards");
        cfg.Badges.LogoPath = Fix(cfg.Badges.LogoPath, "badges");
        foreach (var k in cfg.Badges.Photos.Keys.ToList())
            cfg.Badges.Photos[k] = Fix(cfg.Badges.Photos[k], Path.Combine("badges", "photos"))!;
        foreach (var t in cfg.Templates.Append(cfg.Overlay))
        {
            var before = t.BackgroundImagePath;
            t.BackgroundImagePath = Fix(t.BackgroundImagePath, "templates");
            // Exe ve veri klasörü dışından seçilmiş görsel: kopyası templates'e
            if (t.BackgroundImagePath == before && t.BackgroundImagePath is { } bg && File.Exists(bg)
                && Under(DataDir, bg) is null && Under(baseDir, bg) is null)
            {
                t.BackgroundImagePath = KeepCopy(bg);
                if (t.BackgroundImagePath != bg) changed++;
            }
        }
        return changed;
    }

    /// <summary>Seçilen notasyon kağıdı görselinin veri klasöründeki kopyası (templates); kopyalanamazsa asıl yol.</summary>
    public static string KeepCopy(string source)
    {
        try
        {
            var dir = Path.Combine(DataDir, "templates");
            if (Under(dir, source) is not null) return source;
            Directory.CreateDirectory(dir);
            var name = Path.GetFileNameWithoutExtension(source);
            var ext = Path.GetExtension(source);
            var dst = Path.Combine(dir, name + ext);
            for (int i = 2; File.Exists(dst) && new FileInfo(dst).Length != new FileInfo(source).Length; i++)
                dst = Path.Combine(dir, $"{name}-{i}{ext}");
            if (!File.Exists(dst)) File.Copy(source, dst);
            return dst;
        }
        catch { return source; }
    }

    private static bool SameDir(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary><paramref name="path"/> <paramref name="dir"/> altındaysa göreli yolu; değilse null.</summary>
    private static string? Under(string dir, string path)
    {
        try
        {
            var rel = Path.GetRelativePath(Path.GetFullPath(dir), Path.GetFullPath(path));
            return rel.StartsWith("..") || Path.IsPathRooted(rel) ? null : rel;
        }
        catch { return null; }
    }

    private static void CopyTree(string from, string to)
    {
        try
        {
            if (!Directory.Exists(from)) return;
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                var dst = Path.Combine(to, Path.GetRelativePath(from, file));
                if (!File.Exists(dst)) TryCopy(file, dst);
            }
        }
        catch { /* kopyalanamayan dosyanın eski yolu geçerli kalır */ }
    }

    private static void TryCopy(string from, string to)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(from, to); } catch { }
    }

    // ================= Tek pencere =================
    private static Mutex? _instance;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);

    /// <summary>
    /// Aynı veri klasörüyle ikinci kez açılmayı önler: iki pencere aynı config.json'u yazarsa biri ötekinin
    /// değişikliklerini (şablonlar dahil) ezer. Açık pencere öne getirilir. Güncellemeden sonra eski sürüm
    /// kapanana kadar beklenir. false = program açık, bu süreç kapanmalı.
    /// </summary>
    public static bool AcquireSingleInstance(bool afterUpdate)
    {
        var name = @"Local\ChessScoresheetPrinter-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(DataDir).ToLowerInvariant())))[..16];
        _instance = new Mutex(false, name);
        try
        {
            if (_instance.WaitOne(afterUpdate ? TimeSpan.FromSeconds(30) : TimeSpan.Zero)) return true;
        }
        catch (AbandonedMutexException) { return true; }   // önceki süreç çöktü: kilit bizde
        try
        {
            var me = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(me.ProcessName))
                if (p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(p.MainWindowHandle, 9);   // SW_RESTORE
                    SetForegroundWindow(p.MainWindowHandle);
                    return false;
                }
        }
        catch { }
        MessageBox.Show("Chess Scoresheet Printer zaten açık. Açık pencereyi kullanın (görev çubuğunda).",
                        "Program açık", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }
}
