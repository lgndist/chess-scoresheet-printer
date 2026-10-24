namespace NotasyonOtomasyonu.App;

/// <summary>
/// Ayar dosyasının yeri. Varsayılan %APPDATA%\ChessScoresheetPrinter\config.json: güncellemede, exe
/// başka klasöre taşınınca ya da yeni sürüm ayrı klasöre kurulunca ayarlar ve şablonlar kaybolmaz.
/// Exe yanında "portable" adlı dosya varsa (USB bellekle taşınabilir kullanım) exe yanındaki config.json;
/// CSP_CONFIG_DIR ortam değişkeni verilmişse o klasör (testler) kullanılır. AppData'da henüz ayar yoksa
/// exe yanındaki eski config.json oraya bir kez kopyalanır (eskisi yedek olarak yerinde kalır).
/// </summary>
internal static class AppPaths
{
    public const string FolderName = "ChessScoresheetPrinter";

    /// <param name="migratedFrom">Bu açılışta exe yanından taşındıysa eski dosyanın yolu.</param>
    public static string ConfigPath(string baseDir, out string? migratedFrom)
    {
        migratedFrom = null;
        var local = Path.Combine(baseDir, "config.json");
        var env = Environment.GetEnvironmentVariable("CSP_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            try { Directory.CreateDirectory(env); return Path.Combine(env, "config.json"); }
            catch { /* geçersiz klasör: varsayılana düş */ }
        }
        if (File.Exists(Path.Combine(baseDir, "portable"))) return local;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "config.json");
            if (!File.Exists(path) && File.Exists(local))
            {
                File.Copy(local, path);
                migratedFrom = local;
            }
            return path;
        }
        catch { return local; }   // AppData yazılamıyorsa eskisi gibi exe yanı
    }
}
