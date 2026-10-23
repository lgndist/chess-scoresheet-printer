using System.Globalization;
using System.Text.RegularExpressions;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>Raporun bir alanı: değer, nereden geldiği ve kullanıcıya nasıl sorulacağı.</summary>
public sealed class ReportField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string Value { get; set; } = "";
    public string Hint { get; init; } = "";
    /// <summary>Otomatik: chess-results/sistemden; Önerilen: tahmin/hatırlanan, onay ister; Eksik: sorulmalı.</summary>
    public ReportFieldSource Source { get; set; }
    public bool Optional { get; init; }
    public bool Multiline { get; init; }
    /// <summary>Değerin kaynağı (kullanıcıya gösterilir): "chess-results", "TSF yönergesi", "önceki rapor" …</summary>
    public string Origin { get; set; } = "";
}

/// <summary>chess-results dışındaki bilgi kaynakları.</summary>
public sealed class ReportSources
{
    /// <summary>Turnuva sonu raporu: final sıralamalarından hesaplanan değerler ve tablolar.</summary>
    public EndReportData? EndReport { get; init; }
    /// <summary>Kullanıcının önceki raporlarda verdiği, turnuvadan turnuvaya değişmeyen cevaplar.</summary>
    public IReadOnlyDictionary<string, string> Remembered { get; init; } = new Dictionary<string, string>();
    /// <summary>Bu turnuva için daha önce hazırlanan rapor/tutanakta kullanılan değerler.</summary>
    public IReadOnlyDictionary<string, string>? EventMemory { get; init; }
    /// <summary>TSF il sitesindeki yönergeden okunan bilgiler.</summary>
    public IReadOnlyDictionary<string, string>? Yonerge { get; init; }
    /// <summary>Yönerge bu turnuvanınsa tarih/son başvuru gibi değişen bilgiler de kullanılır.</summary>
    public bool YonergeSameEvent { get; init; }
}

public enum ReportFieldSource { Automatic, Suggested, Missing }

public sealed class ReportCategoryRow
{
    public string Name { get; set; } = "";
    public string Criteria { get; set; } = "";
}

public sealed class ReportProgramRow
{
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
    public string Event { get; set; } = "";
}

/// <summary>Kullanıcıya gösterilecek, doldurulmaya hazır rapor taslağı.</summary>
public sealed class ReportDraft
{
    public List<ReportField> Fields { get; } = new();
    public List<ReportCategoryRow> Categories { get; } = new();
    public List<ReportProgramRow> Program { get; } = new();
    /// <summary>Diğer tablolar (turnuva sonu raporu: katılım, il, derece, sıralama); önek → satırlar.</summary>
    public Dictionary<string, List<Dictionary<string, string>>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasCategoryTable { get; init; }
    public bool HasProgramTable { get; init; }
    /// <summary>chess-results'taki zaman kontrolünden anlaşılan tempo (bilinmiyorsa null).</summary>
    public Tempo? Tempo { get; set; }

    /// <summary>Program başına eklenecek isteğe bağlı kalemler (saat boşsa eklenmez).</summary>
    public static readonly (string Key, string Event)[] PreProgram =
    {
        ("PROGRAM_KAYIT", "Kayıt Kontrol Başlangıç-Bitiş"),
        ("PROGRAM_TEKNIK", "Teknik Toplantı"),
        ("PROGRAM_ILAN", "1. Tur Eşleştirmesinin İlanı"),
    };

    public ReportData ToReportData()
    {
        var d = new ReportData();
        foreach (var f in Fields) d.Values[f.Key] = f.Value?.Trim() ?? "";
        foreach (var c in Categories) d.Categories.Add(new() { ["adi"] = c.Name, ["kriteri"] = c.Criteria });

        // İsteğe bağlı açılış kalemleri: ilk günün başına
        var firstDate = Program.FirstOrDefault()?.Date ?? "";
        foreach (var (key, ev) in PreProgram)
        {
            var t = Fields.FirstOrDefault(f => f.Key == key)?.Value?.Trim();
            if (!string.IsNullOrEmpty(t)) d.Program.Add(new() { ["tarih"] = firstDate, ["saat"] = t!, ["etkinlik"] = ev });
        }
        foreach (var p in Program) d.Program.Add(new() { ["tarih"] = p.Date, ["saat"] = p.Time, ["etkinlik"] = p.Event });
        foreach (var (k, rows) in Lists) d.Lists[k] = rows.Select(r => new Dictionary<string, string>(r)).ToList();
        return d;
    }
}

/// <summary>
/// Yönerge/rapor taslağını chess-results verisinden kurar: turnuva adı, tarih, yer, düşünme süresi,
/// sistem(ler), kategoriler ve kriterleri, direktör/başhakem/hakemler, tur programı. Bulunamayanları
/// (son başvuru, telefon, e-posta …) "Eksik" olarak işaretler; daha önce verilen cevapları önerir.
/// </summary>
public static class ReportBuilder
{
    private static readonly CultureInfo Tr = new("tr-TR");

    /// <summary>Alan kataloğu: anahtar → (soru, ipucu, isteğe bağlı, çok satırlı).</summary>
    public static readonly IReadOnlyDictionary<string, (string Label, string Hint, bool Optional, bool Multi)> Catalog =
        new Dictionary<string, (string, string, bool, bool)>(StringComparer.OrdinalIgnoreCase)
        {
            ["TURNUVA_ADI"] = ("Turnuva adı", "Belgenin başlığında görünür.", false, false),
            ["IL"] = ("İl", "", false, false),
            ["ILCE"] = ("İlçe", "Örn. Merkez", false, false),
            ["TARIH_ARALIGI"] = ("Başlama – bitiş tarihi", "Örn. 31.10.2026 – 01.11.2026", false, false),
            ["YER"] = ("Turnuva yeri", "Salon / adres", false, false),
            ["SON_BASVURU"] = ("Son başvuru tarihi ve saati", "Örn. 18.09.2026 17.00", false, false),
            ["SISTEM"] = ("Sistem", "Kategorilere göre otomatik yazılır.", false, false),
            ["DUSUNME_SURESI"] = ("Düşünme süresi", "Örn. 45 DAKİKA + HAMLE BAŞINA 30 SANİYE EKLEMELİ TEMPO", false, false),
            ["ORGANIZASYON"] = ("Organizasyon", "Örn. Isparta GSİM & Isparta SATRANÇ İL TEMSİLCİLİĞİ", false, false),
            ["SEZON"] = ("Lisans vize sezonu", "Örn. 2026-2027", false, false),
            ["DOGUM_YILLARI"] = ("Katılabilecek doğum yılları", "Örn. 2015-2020", false, false),
            ["KATEGORI_SAYISI"] = ("Kategori sayısı", "", false, false),
            ["BASVURU_ADRESI"] = ("Başvuru adresi", "Örn. https://isparta.tsf.org.tr", false, false),
            ["DIREKTOR"] = ("Yarışma direktörü", "", false, false),
            ["BASHAKEM"] = ("Başhakem", "", false, false),
            ["HAKEMLER"] = ("Hakemler", "", false, true),
            ["TUR_SAYISI"] = ("Tur sayısı", "", false, false),
            ["TELEFON"] = ("İletişim telefonu", "Örn. +90 5xx xxx xx xx", false, false),
            ["EPOSTA"] = ("İletişim e-postası", "", false, false),
            ["PROGRAM_KAYIT"] = ("Kayıt kontrol saati (isteğe bağlı)", "Örn. 09.00-09.30 — boş bırakılırsa programa eklenmez", true, false),
            ["PROGRAM_TEKNIK"] = ("Teknik toplantı saati (isteğe bağlı)", "Örn. 09.30 — boş bırakılırsa eklenmez", true, false),
            ["PROGRAM_ILAN"] = ("1. tur eşleştirme ilanı saati (isteğe bağlı)", "Örn. 09.50 — boş bırakılırsa eklenmez", true, false),
            ["TOPLANTI_TARIHI"] = ("Teknik toplantı tarihi", "Örn. 03.10.2026", false, false),
            ["TOPLANTI_SAATI"] = ("Teknik toplantı saati", "Örn. 09.30", false, false),
            ["TOPLANTI_YERI"] = ("Teknik toplantı yeri", "Turnuva salonu", false, false),
            ["SPORCU_SAYISI"] = ("Toplam sporcu sayısı", "Final sıralamalarından", false, false),
            ["IL_SAYISI"] = ("Katılan il sayısı", "TSF kayıt listesindeki illerden (📋 Kayıt / İl)", true, false),
            ["RAPOR_TARIHI"] = ("Rapor tarihi", "Örn. 04.10.2026", false, false),
            ["DEGERLENDIRME"] = ("Genel değerlendirme / itirazlar", "Turnuvanın işleyişi, itirazlar, olağanüstü durumlar", false, true),
        };

    /// <summary>Kullanıcıdan alınıp bir sonraki raporda önerilecek alanlar.</summary>
    public static readonly HashSet<string> Remembered = new(StringComparer.OrdinalIgnoreCase)
    {
        "ILCE", "ORGANIZASYON", "BASVURU_ADRESI", "TELEFON", "EPOSTA", "PROGRAM_KAYIT", "PROGRAM_TEKNIK", "PROGRAM_ILAN"
    };

    /// <summary>Her turnuvada değişen, belgedeki eski değeri önerilmeyecek alanlar.</summary>
    private static readonly HashSet<string> StaleKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "SON_BASVURU", "TARIH_ARALIGI", "TUR_SAYISI", "KATEGORI_SAYISI", "SISTEM", "DOGUM_YILLARI", "SEZON",
        "TOPLANTI_TARIHI", "TURNUVA_ADI"
    };

    /// <summary>Aynı ilin BAŞKA bir turnuvasının yönergesinden de alınabilecek (sabit kalan) bilgiler.</summary>
    private static readonly HashSet<string> StableKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ORGANIZASYON", "TELEFON", "EPOSTA", "BASVURU_ADRESI", "DIREKTOR", "ILCE",
        "PROGRAM_KAYIT", "PROGRAM_TEKNIK", "PROGRAM_ILAN"
    };

    public static async Task<ReportDraft> BuildAsync(
        ChessResultsService svc, int eventTnr, string eventName, IReadOnlyList<CategoryRef> categories, string? province,
        IReadOnlyDictionary<string, string> remembered, TemplateAnalysis analysis,
        Func<int, Task<(int MaxRound, TournamentSystem System)>> categoryInfo,
        CancellationToken ct = default)
    {
        var info = await svc.GetInfoTableAsync(eventTnr, ct).ConfigureAwait(false);
        List<(int Round, DateTime? Date, string Time)> schedule;
        try { schedule = await svc.GetScheduleAsync(eventTnr, ct).ConfigureAwait(false); }
        catch (Exception) { schedule = new(); }

        var cats = new List<(CategoryRef Cat, int Max, TournamentSystem Sys)>();
        foreach (var c in categories)
        {
            var (max, sys) = await categoryInfo(c.Tnr).ConfigureAwait(false);
            cats.Add((c, max, sys));
        }
        return Build(info, schedule, cats, eventName, province, remembered, analysis);
    }

    public static ReportDraft Build(
        IReadOnlyDictionary<string, string> info,
        IReadOnlyList<(int Round, DateTime? Date, string Time)> schedule,
        IReadOnlyList<(CategoryRef Cat, int Max, TournamentSystem Sys)> cats,
        string eventName, string? province, IReadOnlyDictionary<string, string> remembered, TemplateAnalysis analysis)
        => Build(info, schedule, cats, eventName, province, analysis, new ReportSources { Remembered = remembered });

    /// <summary>Ağ gerektirmeyen kısım (test edilebilir).</summary>
    public static ReportDraft Build(
        IReadOnlyDictionary<string, string> info,
        IReadOnlyList<(int Round, DateTime? Date, string Time)> schedule,
        IReadOnlyList<(CategoryRef Cat, int Max, TournamentSystem Sys)> cats,
        string eventName, string? province, TemplateAnalysis analysis, ReportSources sources)
    {
        string Info(params string[] labels)
        {
            // Önce tam eşleşme ("Başhakem" ≠ "Başhakem Yardımcısı"), sonra önek ("Zaman kontrolü (Rapid)").
            foreach (var l in labels)
                foreach (var (k, v) in info)
                    if (EventGrouping.Fold(k).Trim() == EventGrouping.Fold(l)) return v;
            foreach (var l in labels)
                foreach (var (k, v) in info)
                    if (k.StartsWith(l, StringComparison.OrdinalIgnoreCase)) return v;
            return "";
        }

        var draft = new ReportDraft { HasCategoryTable = analysis.HasCategoryTable, HasProgramTable = analysis.HasProgramTable };
        var days = TournamentDates.Parse(Info("Tarih", "Date"));
        var first = days.Count > 0 ? days[0] : (DateTime?)null;

        // ---- otomatik değerler ----
        var auto = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TURNUVA_ADI"] = TurkishUpper(EventGrouping.BaseName(eventName ?? "")),
            ["TARIH_ARALIGI"] = days.Count == 0 ? "" : DateRange(days[0], days[^1]),
            ["YER"] = Info("Yer", "Location"),
            ["ORGANIZASYON"] = Info("Organizatör", "Organizer"),
            ["DUSUNME_SURESI"] = FormatTimeControl(Info("Zaman kontrol", "Time control")),
            ["DIREKTOR"] = People(Info("Turnuva direkt", "Tournament director")),
            ["BASHAKEM"] = FormatPerson(Info("Başhakem", "Chief Arbiter")),
            ["HAKEMLER"] = People(Info("Hakem", "Arbiter")),
            ["KATEGORI_SAYISI"] = cats.Count > 0 ? cats.Count.ToString() : "",
            ["SISTEM"] = FormatSystems(cats),
            ["TUR_SAYISI"] = cats.Count > 0 ? cats.Max(c => c.Max).ToString() : Info("Tur Sayısı"),
            ["SEZON"] = first is { } f ? Season(f) : "",
        };
        // Turnuva sonu raporu: final sıralamalarından hesaplananlar
        if (sources.EndReport is { } end)
        {
            foreach (var (k, v) in end.Values) if (!string.IsNullOrWhiteSpace(v)) auto[k] = v;
            foreach (var (k, rows) in end.Lists) draft.Lists[k] = rows;
        }
        // İl: seçili il; yoksa turnuva adında ya da yerinde geçen il ("Merkez/Elazığ").
        if (string.IsNullOrWhiteSpace(province)) province = DetectProvince(eventName ?? "", auto["YER"]);
        if (!string.IsNullOrWhiteSpace(province)) auto["IL"] = province!;

        // Kategori satırları ve doğum yılları
        int seasonEnd = first is { } fd ? (fd.Month >= 7 ? fd.Year + 1 : fd.Year) : DateTime.Today.Year;
        var years = new List<int>();
        bool anyUnder = false;
        foreach (var (cat, _, _) in cats)
        {
            var (name, criteria, year) = CategoryCriteria(cat.Name, seasonEnd);
            draft.Categories.Add(new ReportCategoryRow { Name = name, Criteria = criteria });
            if (year is int y) years.Add(y);
            anyUnder |= EventGrouping.Fold(cat.Name).Contains("alti");
        }
        if (years.Count > 0 && cats.All(c => !IsOpen(c.Cat.Name)))
        {
            // "X yaş ve altı": üst sınır en küçük resmi kategori (7 yaş) doğumu.
            int lo = years.Min(), hi = anyUnder ? Math.Max(years.Max(), seasonEnd - YoungestAge) : years.Max();
            auto["DOGUM_YILLARI"] = lo == hi ? lo.ToString() : $"{lo}-{hi}";
        }

        foreach (var (rd, date, time) in schedule.OrderBy(s => s.Round))
            draft.Program.Add(new ReportProgramRow
            {
                Date = date is { } d ? d.ToString("d MMMM yyyy dddd", Tr) : "",
                Time = (time ?? "").Replace(':', '.'),   // yönergelerde "10.00" yazımı
                Event = $"{rd}. Tur"
            });

        // Tempo: chess-results "Zaman kontrolü (Rapid)" etiketinden, yoksa süreden (FIDE tanımı).
        var tcKey = info.Keys.FirstOrDefault(k => k.StartsWith("Zaman kontrol", StringComparison.OrdinalIgnoreCase) || k.StartsWith("Time control", StringComparison.OrdinalIgnoreCase));
        draft.Tempo = TimeControls.FromLabel(tcKey)
                      ?? (TimeControls.Parse(Info("Zaman kontrol", "Time control")) is { } tcv ? TimeControls.Classify(tcv.Minutes, tcv.Increment) : null);
        if (first is { } fday) auto["TOPLANTI_TARIHI"] = fday.ToString("dd.MM.yyyy", Tr);
        if (auto["YER"].Length > 0) auto["TOPLANTI_YERI"] = auto["YER"];

        // ---- önerilenler: düşükten yükseğe öncelik (sonra yazılan kazanır) ----
        //  belgede zaten yazan < ilden tahmin < başka turnuvanın yönergesi (sabit bilgiler)
        //  < son girilen cevaplar < bu turnuvanın yönergesi < bu turnuva için daha önce girilenler.
        // Belgedeki eski TARİHLER önerilmez (geçen turnuvanınkidir): sorulsun.
        var suggested = new Dictionary<string, (string Value, string Origin)>(StringComparer.OrdinalIgnoreCase);
        void Suggest(string k, string? v, string origin) { if (!string.IsNullOrWhiteSpace(v)) suggested[k] = (v.Trim(), origin); }
        foreach (var (k, v) in analysis.Existing) if (!StaleKeys.Contains(k)) Suggest(k, v, "şablonda yazan");
        if (auto.TryGetValue("IL", out var il) && il.Length > 0)
        {
            Suggest("ORGANIZASYON", $"{il} GSİM & {il} SATRANÇ İL TEMSİLCİLİĞİ", "tahmin");
            Suggest("EPOSTA", $"{EventGrouping.Fold(il)}@tsf.org.tr", "tahmin");
            Suggest("BASVURU_ADRESI", $"https://{EventGrouping.Fold(il)}.tsf.org.tr", "tahmin");
            Suggest("ILCE", "Merkez", "tahmin");
        }
        if (sources.Yonerge is { } yon && !sources.YonergeSameEvent)
            foreach (var (k, v) in yon) if (StableKeys.Contains(k)) Suggest(k, v, "TSF yönergesi (başka turnuva)");
        foreach (var (k, v) in sources.Remembered) if (Remembered.Contains(k)) Suggest(k, v, "son girilen");
        if (sources.Yonerge is { } same && sources.YonergeSameEvent)
            foreach (var (k, v) in same)
            {
                // chess-results'ta olan bilgi aynıysa yönergedeki Türkçe yazımı tercih edilir ("DURMUS" → "Durmuş").
                if (auto.TryGetValue(k, out var a) && a.Length > 0)
                {
                    if (EventGrouping.Fold(a).Replace(" ", "") == EventGrouping.Fold(v).Replace(" ", "")) auto[k] = v;
                    continue;
                }
                Suggest(k, v, "TSF yönergesi");
            }
        if (sources.EventMemory is { } mem) foreach (var (k, v) in mem) Suggest(k, v, "bu turnuvada girilen");

        // Turnuva sonu raporu: değerlendirme için başlangıç metni
        Suggest("DEGERLENDIRME", "Turnuva, yönergesine ve TSF kurallarına uygun olarak sorunsuz tamamlanmıştır. Turnuva süresince itiraz olmamıştır.", "öneri");
        // Teknik toplantı saati, programdaki teknik toplantı kaleminden
        if (!suggested.ContainsKey("TOPLANTI_SAATI") && suggested.TryGetValue("PROGRAM_TEKNIK", out var tek))
            Suggest("TOPLANTI_SAATI", tek.Value, tek.Origin);
        // Düşünme süresi bulunamadıysa tempoya göre resmi öneri
        if (auto["DUSUNME_SURESI"].Length == 0 && draft.Tempo is { } tempo)
        {
            var (mn, inc) = TimeControls.Presets(tempo)[0];
            Suggest("DUSUNME_SURESI", TimeControls.Format(mn, inc), $"{tempo.DisplayName()} önerisi");
        }

        // ---- belgede istenen alanlar (+ program tablosu varsa açılış saatleri) ----
        var keys = analysis.Fields.ToList();
        if (analysis.HasProgramTable) keys.AddRange(ReportDraft.PreProgram.Select(p => p.Key));
        // Sorular belgedeki sırayla değil, katalog sırasıyla (turnuva adı, il, tarih … iletişim, program)
        var order = Catalog.Keys.ToList(); // eleman silinmeyen Dictionary ekleme sırasını korur
        int Rank(string k) { int i = order.FindIndex(o => o.Equals(k, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }
        foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Rank))
        {
            var (label, hint, optional, multi) = Catalog.TryGetValue(key, out var c) ? c : (Humanize(key), "", false, false);
            var field = new ReportField { Key = key, Label = label, Hint = hint, Optional = optional, Multiline = multi };
            if (auto.TryGetValue(key, out var a) && a.Length > 0) { field.Value = a; field.Source = ReportFieldSource.Automatic; field.Origin = "chess-results"; }
            else if (suggested.TryGetValue(key, out var s)) { field.Value = s.Value; field.Source = ReportFieldSource.Suggested; field.Origin = s.Origin; }
            else field.Source = ReportFieldSource.Missing;
            draft.Fields.Add(field);
        }
        return draft;
    }

    private static bool IsOpen(string name) => EventGrouping.Fold(name).Contains("acik") || EventGrouping.Fold(name).Contains("open");

    /// <summary>En küçük resmi yaş kategorisi (TSF: 7 yaş).</summary>
    private const int YoungestAge = 7;

    /// <summary>
    /// "7 Yaş Kız" → ("7 Yaş Kız Kategorisi", "2020 Doğumlu Kız Sporcular", 2020). Adın sonunda
    /// "[2020-2017 DOĞUMLU …]" gibi doğum yılı açıklaması varsa kriter olarak o kullanılır.
    /// </summary>
    public static (string Name, string Criteria, int? BirthYear) CategoryCriteria(string category, int seasonEndYear)
    {
        var name = category.Trim();
        string? given = null;
        var bm = Regex.Match(name, @"\s*[\[(]([^\])]*\d{4}[^\])]*)[\])]?\s*$");
        if (bm.Success && bm.Index > 0)
        {
            given = bm.Groups[1].Value.Trim();
            name = name[..bm.Index].Trim();
        }
        var f = EventGrouping.Fold(name);
        bool upper = name.Any(char.IsLetter) && name.Where(char.IsLetter).All(char.IsUpper);
        if (!f.Contains("kategori")) name += upper ? " KATEGORİSİ" : " Kategorisi";
        var m = Regex.Match(f, @"(\d{1,2})\s*yas");
        if (!m.Success)
            return (name, given ?? (IsOpen(category) ? "Tüm sporcular" : ""), null);
        int year = seasonEndYear - int.Parse(m.Groups[1].Value);
        bool girls = f.Contains("kiz") || f.Contains("kadin");
        bool under = f.Contains("alti");
        string who = girls ? "Kız Sporcular" : "Sporcular";
        return (name, given ?? (under ? $"{year} ve Sonrası Doğumlu {who}" : $"{year} Doğumlu {who}"), year);
    }

    /// <summary>
    /// Dar tablo hücresine sığan tarih aralığı: "03-04.10.2026", "31.10-01.11.2026",
    /// "30.12.2026-02.01.2027"; tek gün "03.10.2026".
    /// </summary>
    public static string DateRange(DateTime a, DateTime b)
    {
        if (b < a) (a, b) = (b, a);
        if (a.Date == b.Date) return a.ToString("dd.MM.yyyy", Tr);
        if (a.Year != b.Year) return $"{a.ToString("dd.MM.yyyy", Tr)}-{b.ToString("dd.MM.yyyy", Tr)}";
        if (a.Month != b.Month) return $"{a.ToString("dd.MM", Tr)}-{b.ToString("dd.MM.yyyy", Tr)}";
        return $"{a.ToString("dd", Tr)}-{b.ToString("dd.MM.yyyy", Tr)}";
    }

    /// <summary>"A; B" → "A, B" (her kişi <see cref="FormatPerson"/> ile).</summary>
    public static string People(string s)
        => string.Join(", ", (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(FormatPerson).Where(p => p.Length > 0));

    private static readonly (string Alias, string Province)[] ProvinceAliases =
    {
        ("afyon", "Afyonkarahisar"), ("maras", "Kahramanmaraş"), ("urfa", "Şanlıurfa"), ("antep", "Gaziantep")
    };

    /// <summary>Metinlerde (turnuva adı, yer) geçen il; bulunamazsa null.</summary>
    public static string? DetectProvince(params string?[] texts)
    {
        foreach (var t in texts)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            var f = EventGrouping.Fold(t);
            foreach (var p in Provinces.List.Skip(1))
                if (Regex.IsMatch(f, $@"(?<![\p{{L}}]){Regex.Escape(EventGrouping.Fold(p))}(?![\p{{L}}])")) return p;
            foreach (var (alias, p) in ProvinceAliases)
                if (Regex.IsMatch(f, $@"(?<![\p{{L}}]){alias}(?![\p{{L}}])")) return p;
        }
        return null;
    }

    /// <summary>Lisans sezonu: Temmuz ve sonrası → "2026-2027", öncesi → "2025-2026".</summary>
    public static string Season(DateTime d) => d.Month >= 7 ? $"{d.Year}-{d.Year + 1}" : $"{d.Year - 1}-{d.Year}";

    /// <summary>"35'+30'' Eklemeli Tempo" → "35 DAKİKA + HAMLE BAŞINA 30 SANİYE EKLEMELİ TEMPO".</summary>
    public static string FormatTimeControl(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var m = Regex.Match(s, @"(\d+)\s*'\s*\+\s*(\d+)\s*''");
        if (m.Success) return $"{m.Groups[1].Value} DAKİKA + HAMLE BAŞINA {m.Groups[2].Value} SANİYE EKLEMELİ TEMPO";
        var m2 = Regex.Match(s, @"^(\d+)\s*'");
        if (m2.Success) return $"{m2.Groups[1].Value} DAKİKA";
        return TurkishUpper(s);
    }

    /// <summary>"NA Kutdemir, Mehmet 34570535" → "NA Mehmet KUTDEMİR"; "… Tüm Hakemler" kırpıntısı atılır.</summary>
    public static string FormatPerson(string s)
    {
        s = Regex.Replace(s ?? "", @"\.\.\..*$", "").Trim();
        s = Regex.Replace(s, @"\s*\d{4,}\s*$", "").Trim();      // FIDE/TSF numarası
        if (s.Length == 0) return "";
        string title = "";
        var tm = Regex.Match(s, @"^(IA|FA|NA|IO|FST|FT|IM|GM|FM|CM|WGM|WIM|WFM)\s+");
        if (tm.Success) { title = tm.Groups[1].Value + " "; s = s[tm.Length..]; }
        var parts = s.Split(',', 2);
        if (parts.Length == 2)
            return $"{title}{parts[1].Trim()} {TurkishUpper(parts[0].Trim())}".Trim();
        return (title + s).Trim();
    }

    /// <summary>Kategorilerin sistemlerinden "BİREYSEL İSVİÇRE SİSTEMİ 5 TUR / BİREYSEL BERGER SİSTEMİ (8 Yaş)".</summary>
    public static string FormatSystems(IReadOnlyList<(CategoryRef Cat, int Max, TournamentSystem Sys)> cats)
    {
        if (cats.Count == 0) return "";
        var groups = cats.GroupBy(c => (c.Sys == TournamentSystem.Unknown ? TournamentSystem.Swiss : c.Sys, c.Max)).ToList();
        string Name(TournamentSystem s) => s switch
        {
            TournamentSystem.RoundRobin => "BİREYSEL BERGER SİSTEMİ",
            TournamentSystem.TeamSwiss => "TAKIM İSVİÇRE SİSTEMİ",
            TournamentSystem.TeamRoundRobin => "TAKIM BERGER SİSTEMİ",
            _ => "BİREYSEL İSVİÇRE SİSTEMİ"
        };
        var parts = new List<string>();
        foreach (var g in groups.OrderByDescending(g => g.Count()))
        {
            var (sys, max) = g.Key;
            var text = sys.IsRoundRobin() ? Name(sys) : $"{Name(sys)} {max} TUR";
            if (groups.Count > 1 && g.Count() < cats.Count && (sys.IsRoundRobin() || g.Count() <= cats.Count / 2))
                text += $" ({string.Join(", ", g.Select(c => EventGrouping.ShortCategory(c.Cat.Name)))})";
            parts.Add(text);
        }
        return string.Join(" / ", parts);
    }

    private static string Humanize(string key) => Tr.TextInfo.ToTitleCase(key.Replace('_', ' ').ToLower(Tr));

    public static string TurkishUpper(string s) => (s ?? "").ToUpper(Tr);
}
