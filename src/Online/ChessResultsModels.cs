namespace NotasyonOtomasyonu.Online;

/// <summary>Federasyon listesindeki bir turnuva girişi (tnr + ad).</summary>
public record TournamentRef(int Tnr, string Name);

/// <summary>Bir etkinliğin bir kategorisi/grubu (ör. "A Kategorisi"). Her kategori ayrı tnr'dir.</summary>
public record CategoryRef(int Tnr, string Name, bool IsCurrent);

/// <summary>
/// Bir chess-results etkinlik (tnr) sayfasından çıkarılan üst bilgi:
/// ad, tarih, tur sayısı, eşlenmiş son tur ve tüm kategoriler.
/// </summary>
/// <param name="MaxRound">Toplam tur sayısı (bilinmiyorsa makul bir üst sınır).</param>
/// <param name="CurrentRound">Eşleştirmesi yayımlanmış son tur; henüz hiç tur yoksa 0.</param>
/// <param name="System">Kategorinin sistemi ("Turnuva Tipi"): İsviçre, Berger, takım…</param>
public record EventInfo(
    int Tnr,
    string Name,
    string? Dates,
    int MaxRound,
    int CurrentRound,
    IReadOnlyList<CategoryRef> Categories,
    NotasyonOtomasyonu.Core.TournamentSystem System = NotasyonOtomasyonu.Core.TournamentSystem.Unknown,
    int RankedRound = 0); // sıralaması yayımlanmış son tur ("5. Turdan Sonra Sıralama"); yoksa 0

/// <summary>chess-results'tan okunmuş, olduğu gibi basılacak bir tablo (ör. tur sonrası sıralama).</summary>
public sealed record ListTable(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);
