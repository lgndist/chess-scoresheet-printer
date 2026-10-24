using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Ayar dosyası güvenliği: yedekli kayıt, bozuk dosyada yedekten okuma, bozuk dosyanın saklanması.</summary>
public class ConfigSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"csp-cfg-{Guid.NewGuid():N}");
    private string Path_ => Path.Combine(_dir, "config.json");

    public ConfigSafetyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static AppConfig WithTemplate(string name)
    {
        var c = new AppConfig();
        c.Templates.Add(new OverlayTemplate { Name = name, Fields = { new OverlayField { X = 0.1, Y = 0.2 } } });
        return c;
    }

    [Fact]
    public void Save_KeepsPreviousAsBackup()
    {
        WithTemplate("Birinci").Save(Path_);
        WithTemplate("İkinci").Save(Path_);
        Assert.Equal("İkinci", AppConfig.Load(Path_).Templates[0].Name);
        Assert.True(File.Exists(Path_ + ".bak"));
        Assert.False(File.Exists(Path_ + ".tmp"));
    }

    [Fact]
    public void CorruptFile_LoadsBackup_AndKeepsCorruptCopy()
    {
        WithTemplate("Sağlam").Save(Path_);
        WithTemplate("Son").Save(Path_);                 // .bak = "Sağlam"
        File.WriteAllText(Path_, "{ \"Templates\": [ { \"Name\": \"yar");   // yarım yazılmış
        var cfg = AppConfig.Load(Path_);
        Assert.Equal("Sağlam", cfg.Templates[0].Name);   // şablonlar sıfırlanmadı
        Assert.Single(Directory.GetFiles(_dir, "config.json.bozuk-*"));
    }

    [Fact]
    public void MissingFile_WithBackup_LoadsBackup()
    {
        WithTemplate("A").Save(Path_);
        WithTemplate("B").Save(Path_);
        File.Delete(Path_);
        Assert.Equal("A", AppConfig.Load(Path_).Templates[0].Name);
    }

    [Fact]
    public void NothingThere_Defaults()
        => Assert.Empty(AppConfig.Load(Path_).Templates);
}
