namespace NotasyonOtomasyonu.Core;

/// <summary>
/// Kategorinin oynandığı sistem. chess-results'ta kategori sayfasındaki "Turnuva Tipi" satırından okunur.
/// Sistem; eşleştirmenin nasıl okunacağını (tur bölümleri, takım maçları) ve hangi şablonun
/// kullanılacağını belirler.
/// </summary>
public enum TournamentSystem
{
    Unknown,
    Swiss,          // İsviçre sistemi: her tur ayrı sayfada eşlenir
    RoundRobin,     // Döner (Berger): tüm turlar baştan bellidir, tek sayfada listelenir
    TeamSwiss,      // Takımlar için İsviçre sistemi
    TeamRoundRobin  // Takımlar için döner turnuva
}

public static class TournamentSystems
{
    public static bool IsTeam(this TournamentSystem s) => s is TournamentSystem.TeamSwiss or TournamentSystem.TeamRoundRobin;
    public static bool IsRoundRobin(this TournamentSystem s) => s is TournamentSystem.RoundRobin or TournamentSystem.TeamRoundRobin;

    /// <summary>Şablon eşlemesinde kullanılan sade grup: Swiss / RoundRobin / Team.</summary>
    public static string TemplateKey(this TournamentSystem s) => s switch
    {
        TournamentSystem.RoundRobin => "RoundRobin",
        TournamentSystem.TeamSwiss or TournamentSystem.TeamRoundRobin => "Team",
        _ => "Swiss"
    };

    public static string DisplayName(this TournamentSystem s) => s switch
    {
        TournamentSystem.Swiss => "İsviçre",
        TournamentSystem.RoundRobin => "Berger (döner)",
        TournamentSystem.TeamSwiss => "Takım (İsviçre)",
        TournamentSystem.TeamRoundRobin => "Takım (döner)",
        _ => "Bilinmiyor"
    };
}

/// <summary>
/// Tek bir oyuncu. Eksik alanlar (rating, unvan, kulüp) null/0 olabilir; render bunları boş bırakır.
/// </summary>
public record Player(
    int? StartNo,        // Sıra no (SNo / StartNo)
    string Name,         // Ad Soyad (zorunlu)
    string? Title = null,// Unvan (GM/IM/FM…)
    int? Rating = null,  // ELO (0/boş olabilir)
    string? Federation = null,
    string? Club = null,
    string? Province = null) // il (TSF kayıt listesinden; kağıttaki "İl / City" kutusu)
{
    /// <summary>"FM Ali Veli (12) 2300" gibi tek satır gösterim için yardımcı.</summary>
    public string DisplayLine()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Title)) parts.Add(Title!.Trim());
        parts.Add(string.IsNullOrWhiteSpace(Name) ? "—" : Name.Trim());
        return string.Join(" ", parts);
    }

    /// <summary>BAY (bye) oyuncusunu temsil eden sahte oyuncu.</summary>
    public static Player Bye() => new(null, "BAY");
}

/// <summary>
/// Bir masadaki eşleştirme. <see cref="Black"/> null ise oyuncu BAY almıştır.
/// <see cref="Board"/> bir tur içinde masayı TEKİL olarak tanımlar (seçim/hariç tutma buna göre);
/// takım turnuvasında kağıda basılan masa "3.2" gibi <see cref="BoardLabel"/>'dır.
/// </summary>
public record Pairing(
    int Board,           // Masa no (tur içinde tekil)
    Player White,
    Player? Black,       // null = BAY (bye)
    string? Result = null, // genelde boş
    string? Category = null, // ait olduğu kategori (kısa: "A", "8 Yaş") — notasyona basmak için
    int? Round = null,   // tur no; kategoriler farklı turdaysa (toplu baskı) masaya özgü tur
    string? BoardLabel = null,  // takım: "maç.masa" (ör. "3.2"); bireysel: null → Board basılır
    string? WhiteTeam = null,   // takım: beyaz oyuncunun takımı
    string? BlackTeam = null,   // takım: siyah oyuncunun takımı
    TournamentSystem System = TournamentSystem.Unknown, // şablon seçimi için
    string? WhitePoints = null, // tur başındaki puan ("2½"); eşleştirme listesi için
    string? BlackPoints = null)
{
    public bool IsBye => Black is null;

    /// <summary>Kağıda/listeye yazılan masa: takımda "3.2", bireyselde "7".</summary>
    public string BoardText => string.IsNullOrWhiteSpace(BoardLabel) ? Board.ToString() : BoardLabel!;
}

/// <summary>
/// Bir turun tüm bilgisi. Pairings her zaman masa no'ya göre artan sırada tutulur.
/// Yer/hakem/zaman kontrolü bilinçli olarak tutulmaz: kağıtta bu alanlar boş kalır.
/// </summary>
public record Tournament(
    string Name,
    int RoundNo,
    IReadOnlyList<Pairing> Pairings,
    string? Date = null,
    TournamentSystem System = TournamentSystem.Unknown);
