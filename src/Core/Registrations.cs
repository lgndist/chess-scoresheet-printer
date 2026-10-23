namespace NotasyonOtomasyonu.Core;

/// <summary>TSF il sitesindeki turnuva kayıt listesinin bir satırı (herkese açık sütunlar).</summary>
public sealed class PlayerRegistration
{
    public string Given { get; set; } = "";     // AD ("MEHMET ARİF")
    public string Surname { get; set; } = "";   // SOYAD ("AVCI")
    public string Province { get; set; } = "";  // İL ("ISPARTA")
    public int? Elo { get; set; }
    public int? Ukd { get; set; }
    public string Category { get; set; } = "";  // KATEGORİ ("12 Yaş ve Altı")
    public int Tid { get; set; }                // hangi kayıt formundan

    public string FullName => $"{Given} {Surname}".Trim();
}

/// <summary>
/// Bir etkinlik için turnuva başında bir kez alınan kayıt listesi ve elle yapılan il düzeltmeleri.
/// Kayıt kapanınca sitedeki liste kaybolduğundan liste burada saklanır; tekrar tekrar çekilmez.
/// </summary>
public sealed class EventRegistration
{
    public string Site { get; set; } = "";
    /// <summary>Listenin bağlandığı chess-results turnuvasının adı (hangi turnuva için alındığı görünsün).</summary>
    public string EventName { get; set; } = "";
    public List<int> Tids { get; set; } = new();
    public List<string> Titles { get; set; } = new();
    public DateTime FetchedAt { get; set; }
    public List<PlayerRegistration> Entries { get; set; } = new();

    /// <summary>Kontrol ekranında onaylanan eşleşmeler (sporcu anahtarı → il); baskıda önce buna bakılır.</summary>
    public Dictionary<string, string> Resolved { get; set; } = new();

    /// <summary>Elle verilen il (sporcu anahtarı → il; "" = bilerek boş). Eşleşmenin önüne geçer.</summary>
    public Dictionary<string, string> Overrides { get; set; } = new();
}
