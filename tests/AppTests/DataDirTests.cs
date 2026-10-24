using NotasyonOtomasyonu.App;
using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>
/// Kullanıcı dosyalarının veri klasörüne taşınması: exe klasöründeki afiş/logo/fotoğraf/rapor/kağıt
/// görselleri kopyalanır, yollar güncellenir; dışarıdan seçilen kağıt görseli kopyalanır.
/// AppPaths durumu statik olduğundan bu sınıf ayrı bir koleksiyonda sırayla çalışır.
/// </summary>
[Collection("AppPaths")]
public class DataDirTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csp-data-{Guid.NewGuid():N}");
    private string Exe => Path.Combine(_root, "exe");
    private string Data => Path.Combine(_root, "data");
    private string Outside => Path.Combine(_root, "indirilenler");

    public DataDirTests()
    {
        foreach (var d in new[] { Exe, Data, Outside }) Directory.CreateDirectory(d);
        Environment.SetEnvironmentVariable("CSP_CONFIG_DIR", Data);
        AppPaths.Init(Exe);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CSP_CONFIG_DIR", null);
        try { Directory.Delete(_root, true); } catch { }
    }

    private static string Touch(string path, string text = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Init_UsesOverrideFolder()
    {
        Assert.Equal(Data, AppPaths.DataDir);
        Assert.Equal(Path.Combine(Data, "config.json"), AppPaths.ConfigFile);
    }

    [Fact]
    public void ExeFolderFiles_AreCopied_AndPathsRewritten()
    {
        var cfg = new AppConfig();
        cfg.Cards.LogoPath = Touch(Path.Combine(Exe, "cards", "afis_1.png"));
        cfg.Badges.LogoPath = Touch(Path.Combine(Exe, "badges", "logo.png"));
        cfg.Badges.Photos["ali"] = Touch(Path.Combine(Exe, "badges", "photos", "ali.jpg"));
        Touch(Path.Combine(Exe, "reports", "benim_raporum.docx"));
        cfg.Overlay.BackgroundImagePath = Touch(Path.Combine(Exe, "templates", "AnaOrnek.png"));

        int changed = AppPaths.MigrateAssets(cfg, Exe);

        Assert.Equal(Path.Combine(Data, "cards", "afis_1.png"), cfg.Cards.LogoPath);
        Assert.Equal(Path.Combine(Data, "badges", "logo.png"), cfg.Badges.LogoPath);
        Assert.Equal(Path.Combine(Data, "badges", "photos", "ali.jpg"), cfg.Badges.Photos["ali"]);
        Assert.Equal(Path.Combine(Data, "templates", "AnaOrnek.png"), cfg.Overlay.BackgroundImagePath);
        Assert.True(File.Exists(Path.Combine(Data, "reports", "benim_raporum.docx")));   // rapor şablonları adla tutulur
        Assert.All(new[] { cfg.Cards.LogoPath, cfg.Badges.LogoPath, cfg.Overlay.BackgroundImagePath }, p => Assert.True(File.Exists(p)));
        Assert.Equal(4, changed);                            // 4 yol (rapor şablonu adla tutulur, yolu değişmez)
        Assert.Equal(0, AppPaths.MigrateAssets(cfg, Exe));   // ikinci açılışta değişiklik yok
    }

    [Fact]
    public void OutsideBackground_IsKeptAsCopy()
    {
        var cfg = new AppConfig();
        var src = Touch(Path.Combine(Outside, "tsf_formu.png"), "kagit");
        cfg.Templates.Add(new OverlayTemplate { Name = "Benim", BackgroundImagePath = src });

        AppPaths.MigrateAssets(cfg, Exe);
        var kept = cfg.Templates[0].BackgroundImagePath!;
        Assert.StartsWith(Path.Combine(Data, "templates"), kept);
        File.Delete(src);                                    // asıl dosya silinse de şablon bozulmaz
        Assert.Equal("kagit", File.ReadAllText(kept));
    }

    [Fact]
    public void BrokenPath_RelinksToSameNameInDataFolder()
    {
        var cfg = new AppConfig();
        Touch(Path.Combine(Data, "cards", "afis_2.png"));
        cfg.Cards.LogoPath = Path.Combine(_root, "silinmis-eski-klasor", "cards", "afis_2.png");
        AppPaths.MigrateAssets(cfg, Exe);
        Assert.Equal(Path.Combine(Data, "cards", "afis_2.png"), cfg.Cards.LogoPath);
    }

    [Fact]
    public void KeepCopy_SameNameDifferentFile_GetsSuffix()
    {
        var a = Touch(Path.Combine(Outside, "a", "kagit.png"), "bir");
        var b = Touch(Path.Combine(Outside, "b", "kagit.png"), "ikinci");
        var ka = AppPaths.KeepCopy(a);
        var kb = AppPaths.KeepCopy(b);
        Assert.NotEqual(ka, kb);
        Assert.Equal("bir", File.ReadAllText(ka));
        Assert.Equal("ikinci", File.ReadAllText(kb));
    }
}
