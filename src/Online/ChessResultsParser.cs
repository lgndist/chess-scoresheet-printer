using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// chess-results.com HTML sayfalarını ayrıştırır (AngleSharp). Türkçe karakterler
/// HTML entity (&amp;#304;) olarak gelir; AngleSharp bunları otomatik çözer.
/// </summary>
public static class ChessResultsParser
{
    private static readonly HtmlParser Parser = new();

    // ---- 1) Federasyon turnuva listesi (fed.aspx) ----
    public static IReadOnlyList<TournamentRef> ParseFederationList(string html)
    {
        var doc = Parser.ParseDocument(html);
        var result = new List<TournamentRef>();
        var seen = new HashSet<int>();

        foreach (var a in doc.QuerySelectorAll("a[href]"))
        {
            var href = a.GetAttribute("href") ?? "";
            var m = Regex.Match(href, @"[Tt]nr(\d+)\.aspx", RegexOptions.IgnoreCase);
            if (!m.Success) continue;

            var name = Clean(a.TextContent);
            if (name.Length < 5) continue;                 // nav/ikon linklerini ele
            if (!int.TryParse(m.Groups[1].Value, out var tnr)) continue;
            if (!seen.Add(tnr)) continue;

            // Arama sonuçlarında satırda "den itibaren / e kadar" tarihleri ("2026/10/17") bulunur.
            var dates = (a.Closest("tr")?.Children ?? Enumerable.Empty<IElement>())
                .Select(td => Clean(td.TextContent))
                .Select(t => DateTime.TryParseExact(t, "yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d) ? d : (DateTime?)null)
                .Where(d => d is not null).ToList();
            result.Add(new TournamentRef(tnr, name, dates.ElementAtOrDefault(0), dates.ElementAtOrDefault(1) ?? dates.ElementAtOrDefault(0)));
        }
        return result;
    }

    // ---- 2) Etkinlik üst bilgisi: ad, tarih, sistem, tur sayısı, kategoriler ----
    // Yer/hakem/zaman kontrolü bilinçli olarak okunmaz: notasyon kağıdına basılmıyorlar.
    public static EventInfo ParseEventInfo(string html, int tnr) => ParseEventInfo(Parser.ParseDocument(html), tnr);

    private static EventInfo ParseEventInfo(IDocument doc, int tnr)
    {
        var name = Clean(doc.QuerySelector("h2")?.TextContent
                          ?? doc.QuerySelector("title")?.TextContent
                          ?? $"Turnuva {tnr}");

        var dates = InfoField(doc, "Tarih", "Date");
        var system = ParseSystem(InfoField(doc, "Turnuva Tipi", "Tournament type", "Turnierart"));

        var (maxRound, currentRound) = ParseRounds(doc);
        // "Tur Sayısı" satırı turnuva başlamadan da toplam turu verir (menüde henüz tur yokken).
        if (int.TryParse(InfoField(doc, "Tur Sayısı", "Number of rounds", "Rundenanzahl"), out var declared) &&
            declared is > 0 and < 100 && (maxRound == UnknownMaxRound || declared > maxRound))
            maxRound = Math.Max(declared, currentRound);
        var categories = ParseCategories(doc, tnr, name);

        return new EventInfo(tnr, name, dates, maxRound, currentRound, categories, system, RankedRound(doc));
    }

    private static readonly Regex RankedTitle = new(
        @"(\d+)\.\s*Turdan\s+Sonra|after\s+Round\s+(\d+)|nach\s+(?:der\s+)?(\d+)\.\s*Runde", RegexOptions.IgnoreCase);

    /// <summary>Sayfa başlığındaki "5. Turdan Sonra Sıralama" → 5 (sonucu girilmiş son tur); yoksa 0.</summary>
    private static int RankedRound(IDocument doc)
    {
        foreach (var h in doc.QuerySelectorAll("h2, h3"))
        {
            var m = RankedTitle.Match(Clean(h.TextContent));
            if (m.Success) return int.Parse(m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);
        }
        return 0;
    }

    /// <summary>
    /// Tur sonrası sıralama (art=1&amp;rd=N): başlık ("4. Turdan Sonra Sıralama") ve tablo. Panoya
    /// basılırken gereksiz sütunlar (bayrak/boş başlık, FIDE ID, FED) ve hep sıfır olan eşitlik
    /// bozma sütunları atılır.
    /// </summary>
    public static ListTable? ParseStandings(string html)
    {
        var doc = Parser.ParseDocument(html);
        var table = doc.QuerySelector("table.CRs1");
        if (table is null) return null;
        var rows = DirectRows(table).ToList();
        int h = rows.FindIndex(r => r.Children.Any(c => c.TagName == "TH") || (r.ClassName ?? "").StartsWith("CRg"));
        if (h < 0) return null;
        var head = RowCells(rows[h]);
        var body = rows.Skip(h + 1).Select(RowCells).Where(c => c.Count == head.Count && c.Any(x => x.Length > 0)).ToList();

        var drop = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FED", "FIDE ID", "FIDE-ID", "ID", "Typ", "Tip", "Grup", "sex", "K" };
        // Başlıksız sütun: bayrak (metinsiz) atılır, unvan (FM, WIM…) doluysa kalır.
        var keep = Enumerable.Range(0, head.Count).Where(i =>
            (head[i].Length > 0 || body.Any(r => r[i].Length > 0)) && !drop.Contains(head[i]) &&
            !(Regex.IsMatch(head[i], @"^(EB|TB|BH|SB)\d*$", RegexOptions.IgnoreCase) && body.All(r => r[i] is "0" or "" or "0,0"))).ToList();

        var title = doc.QuerySelectorAll("h2").Select(x => Clean(x.TextContent)).FirstOrDefault(t => RankedTitle.IsMatch(t))
                    ?? "Sıralama";
        return new ListTable(title, keep.Select(i => head[i]).ToList(),
            body.Select(r => (IReadOnlyList<string>)keep.Select(i => r[i]).ToList()).ToList());
    }

    /// <summary>"Turnuva Tipi" metnini sisteme çevirir (Türkçe/İngilizce/Almanca sayfa metinleri).</summary>
    public static TournamentSystem ParseSystem(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return TournamentSystem.Unknown;
        var t = EventGrouping.Fold(text);
        bool team = t.Contains("takim") || t.Contains("team") || t.Contains("mannschaft");
        bool rr = t.Contains("doner") || t.Contains("round robin") || t.Contains("round-robin") ||
                  t.Contains("rundenturnier") || t.Contains("berger");
        if (team) return rr ? TournamentSystem.TeamRoundRobin : TournamentSystem.TeamSwiss;
        if (rr) return TournamentSystem.RoundRobin;
        if (t.Contains("isvicre") || t.Contains("swiss") || t.Contains("schweizer")) return TournamentSystem.Swiss;
        return TournamentSystem.Unknown;
    }

    // ---- 3) Eşleştirme tablosu -> Tournament ----
    /// <summary>
    /// Eşleştirme sayfasını okuyup YALNIZCA istenen turun masalarını üretir. Döner (Berger)
    /// turnuvada chess-results tüm turları tek sayfada "1. Tur", "2. Tur" … bölümleriyle verir;
    /// bölümler ayrılmazsa turlar karışır (aynı masa no her turda tekrarlanır).
    /// </summary>
    public static Tournament ParsePairings(string html, int tnr, int round,
                                           TournamentSystem system = TournamentSystem.Unknown)
    {
        var doc = Parser.ParseDocument(html);
        var info = ParseEventInfo(doc, tnr); // aynı sayfada üst bilgi de var
        if (system == TournamentSystem.Unknown) system = info.System;

        if (system.IsTeam() || IsTeamBoardPage(doc))
            return ParseTeamBoards(doc, info, round, system.IsTeam() ? system : TournamentSystem.TeamSwiss);

        var sections = ReadSections(doc);
        if (sections.Count == 0)
            throw new InvalidOperationException(
                "Eşleştirme tablosu bulunamadı. Tur numarası geçerli mi? (Henüz oluşturulmamış olabilir.)");

        // Bölüm başlığı varsa yalnızca istenen tur; yoksa (eski/tek bölümlü sayfa) hepsi.
        var chosen = sections.Any(s => s.Round is not null)
            ? sections.Where(s => s.Round == round).ToList()
            : sections;
        if (chosen.Count == 0)
            throw new InvalidOperationException($"{round}. tur bu sayfada yok (henüz eşlenmemiş olabilir).");

        var pairings = chosen.SelectMany(s => s.Pairings).Select(p => p with { System = system }).ToList();
        return new Tournament(
            Name: info.Name,
            RoundNo: round,
            Pairings: pairings,
            Date: info.Dates,
            System: system);
    }

    /// <summary>Döner turnuva sayfasındaki tüm turlar: (tur no, masalar). Tur bilgisini çözmek için.</summary>
    public static IReadOnlyList<RoundSection> ParseRoundSections(string html)
        => ReadSections(Parser.ParseDocument(html));

    /// <summary>Bir sayfadaki tek tur bölümü.</summary>
    public sealed record RoundSection(int? Round, IReadOnlyList<Pairing> Pairings);

    private static readonly Regex SectionTitle = new(
        @"^\s*(?:(\d{1,2})\s*\.\s*(?:Tur|Runde|Round|Rd)\b|(?:Tur|Runde|Round|Rd\.?)\s*(\d{1,2})\b)",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Veri satırlarını sayfa sırasıyla okuyup "N. Tur" başlıklarına göre bölümlere ayırır.
    /// Masa no bölüm içinde tekildir; satır numarası yedek masa no olarak kullanılır.
    /// </summary>
    private static List<RoundSection> ReadSections(IDocument doc)
    {
        var tables = doc.QuerySelectorAll("tr.CRng1, tr.CRng2")
            .Select(r => r.Closest("table")).Where(t => t is not null).Distinct().ToList();
        var result = new List<RoundSection>();
        foreach (var table in tables)
        {
            var map = new PairingColumnMap(FindHeaderCells(table!));
            int? round = null;
            var current = new List<Pairing>();
            int fallback = 0;
            void Flush()
            {
                if (current.Count > 0) result.Add(new RoundSection(round, current));
                current = new List<Pairing>();
                fallback = 0;
            }
            foreach (var row in DirectRows(table!))
            {
                if (!row.ClassList.Contains("CRng1") && !row.ClassList.Contains("CRng2"))
                {
                    var m = SectionTitle.Match(Clean(row.TextContent));
                    if ((row.ClassName ?? "").StartsWith("CRg") && m.Success)
                    {
                        Flush();
                        round = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                    }
                    continue;
                }
                fallback++;
                var cells = RowCells(row);
                if (cells.Count == 0) continue;
                var pairing = map.MapRow(cells, fallback);
                if (pairing is not null) current.Add(pairing);
            }
            Flush();
        }
        return result;
    }

    /// <summary>Tablonun kendi satırları (hücre içindeki iç içe tabloların satırları hariç).</summary>
    private static IEnumerable<IElement> DirectRows(IElement table)
        => table is AngleSharp.Html.Dom.IHtmlTableElement t
            ? t.Rows
            : table.QuerySelectorAll("tr").Where(r => r.Closest("table") == table);

    // ---- 4) Takım turnuvası: maç bazlı masa eşleştirmeleri (art=3) ----
    private static readonly Regex TeamBoardLabel = new(@"^\d{1,3}\.\d{1,2}$");

    /// <summary>Sayfada "1.1, 1.2 …" etiketli masa satırları var mı? (takım masa eşleştirmesi)</summary>
    private static bool IsTeamBoardPage(IDocument doc)
        => doc.QuerySelectorAll("tr.CRng1, tr.CRng2").Take(20)
              .Any(r => r.Children.FirstOrDefault() is { } c && TeamBoardLabel.IsMatch(Clean(c.TextContent)));

    /// <summary>Takım maç listesi sayfası mı (art=2: "Takım | Takım | Sonuç")? O zaman masa sayfası çekilmeli.</summary>
    public static bool IsTeamMatchList(string html)
    {
        var doc = Parser.ParseDocument(html);
        foreach (var tr in doc.QuerySelectorAll("tr.CRng1b, tr.CRg1b, tr.CRng1, tr.CRng2").Take(10))
        {
            var cells = RowCells(tr).Select(EventGrouping.Fold).ToList();
            if (cells.Count(c => c is "takim" or "team" or "mannschaft") >= 2) return true;
        }
        return false;
    }

    /// <summary>
    /// Takım masa sayfası: her maç başlığında ev sahibi / deplasman takımı, altında "maç.masa"
    /// satırları. Kimin beyaz olduğu hücredeki renk işaretinden (FarbewT/FarbesT) okunur;
    /// işaret yoksa takım satrancı kuralı: ilk takım tek numaralı masalarda beyaz.
    /// </summary>
    private static Tournament ParseTeamBoards(IDocument doc, EventInfo info, int round, TournamentSystem system)
    {
        var pairings = new List<Pairing>();
        var foundRounds = new HashSet<int>();
        foreach (var table in doc.QuerySelectorAll("tr.CRng1, tr.CRng2")
                     .Select(r => r.Closest("table")).Where(t => t is not null).Distinct())
        {
            int? sectionRound = null;
            string home = "", away = "";
            foreach (var row in DirectRows(table!))
            {
                var cells = row.Children.Where(c => c.TagName is "TD" or "TH").ToList();
                var texts = cells.Select(c => Clean(c.TextContent)).ToList();
                if (texts.Count == 0) continue;

                var title = SectionTitle.Match(texts[0]);
                if ((row.ClassName ?? "").StartsWith("CRg") && title.Success)
                {
                    sectionRound = int.Parse(title.Groups[1].Success ? title.Groups[1].Value : title.Groups[2].Value);
                    foundRounds.Add(sectionRound.Value);
                    continue;
                }
                if (sectionRound is int sr && sr != round) continue;

                int dash = texts.IndexOf("-");
                if (dash < 3 || dash + 2 >= texts.Count) continue;

                if (!TeamBoardLabel.IsMatch(texts[0]))
                {
                    // Maç başlığı: Masa | no | EV SAHİBİ | Rtg | - | no | DEPLASMAN | Rtg | skor
                    home = texts[dash - 2];
                    away = texts[dash + 2];
                    continue;
                }

                var label = texts[0];
                string homeName = texts[dash - 2], awayName = texts[dash + 2];
                if (homeName.Length == 0 || awayName.Length == 0) continue; // boş masa: oyun yok

                var homePlayer = new Player(null, homeName, NullIfEmpty(texts[dash - 3]), ParseRating(texts[dash - 1]));
                var awayPlayer = new Player(null, awayName, NullIfEmpty(texts[dash + 1]),
                    dash + 3 < texts.Count ? ParseRating(texts[dash + 3]) : null);

                bool homeWhite = ColorOf(cells[dash - 2]) switch
                {
                    'w' => true,
                    's' => false,
                    _ => ColorOf(cells[dash + 2]) switch
                    {
                        'w' => false,
                        's' => true,
                        _ => int.Parse(label[(label.IndexOf('.') + 1)..]) % 2 == 1
                    }
                };

                // Sonuç ev sahibi–deplasman sırasıyla yazılır; beyaz–siyah sırasına çevir.
                var result = NullIfEmpty(texts[^1]);
                if (!homeWhite && result is not null)
                {
                    var parts = result.Split('-', 2);
                    if (parts.Length == 2) result = $"{parts[1].Trim()} - {parts[0].Trim()}";
                }

                pairings.Add(new Pairing(
                    Board: pairings.Count + 1,
                    White: homeWhite ? homePlayer : awayPlayer,
                    Black: homeWhite ? awayPlayer : homePlayer,
                    Result: result,
                    BoardLabel: label,
                    WhiteTeam: homeWhite ? home : away,
                    BlackTeam: homeWhite ? away : home,
                    System: system));
            }
        }

        if (pairings.Count == 0)
            throw new InvalidOperationException(foundRounds.Count > 0 && !foundRounds.Contains(round)
                ? $"{round}. tur bu sayfada yok (henüz eşlenmemiş olabilir)."
                : "Takım masa eşleştirmesi bulunamadı. Tur henüz eşlenmemiş olabilir.");

        return new Tournament(info.Name, round, pairings, info.Dates, system);
    }

    /// <summary>Hücredeki renk işareti: 'w' beyaz, 's' siyah (Almanca weiß/schwarz), yoksa '?'.</summary>
    private static char ColorOf(IElement cell)
    {
        var cls = cell.QuerySelector("div[class^=Farbe]")?.ClassName ?? "";
        return cls.StartsWith("Farbew", StringComparison.OrdinalIgnoreCase) ? 'w'
             : cls.StartsWith("Farbes", StringComparison.OrdinalIgnoreCase) ? 's' : '?';
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static int? ParseRating(string s)
        => int.TryParse(new string(s.Trim().TakeWhile(char.IsDigit).ToArray()), out var n) && n > 0 ? n : null;

    // ---- 5) Rapor/yönerge için: bilgi tablosunun tamamı ve tur programı ----
    /// <summary>
    /// Bilgi tablosundaki tüm "etiket → değer" satırları (Turnuva direktoru, Başhakem, Hakem, Yer,
    /// Zaman kontrolü, Tur Sayısı …). Tam hakem listesi için sayfa turdet=ALL ile alınmalıdır.
    /// </summary>
    public static Dictionary<string, string> ParseInfoTable(string html)
    {
        var doc = Parser.ParseDocument(html);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tr in doc.QuerySelectorAll("tr"))
        {
            var cells = tr.Children.Where(c => c.TagName == "TD").ToList();
            if (cells.Count != 2) continue;
            var label = Clean(cells[0].TextContent);
            var value = Clean(cells[1].TextContent);
            if (label.Length is > 0 and < 40 && !char.IsDigit(label[0]) && value.Length > 0)
                result.TryAdd(label, value);
        }
        return result;
    }

    /// <summary>Başlangıç sıralaması (art=0): sporcu adı ("SOYAD, AD") ve puanı.</summary>
    public static List<Player> ParseStartingList(string html)
    {
        var doc = Parser.ParseDocument(html);
        var list = new List<Player>();
        foreach (var table in doc.QuerySelectorAll("table.CRs1"))
        {
            var rows = table.QuerySelectorAll("tr").ToList();
            int h = rows.FindIndex(r => r.Children.Any(c => c.TagName == "TH") ||
                                        RowCells(r).Any(c => c is "İsim" or "Name" or "Ad"));
            if (h < 0) continue;
            var head = RowCells(rows[h]);
            int iName = head.FindIndex(c => c is "İsim" or "Name" or "Ad" or "Oyuncu");
            int iNo = head.FindIndex(c => c is "No." or "No" or "Sno");
            int iRtg = head.FindIndex(c => Regex.IsMatch(c, @"^(Rtg|ELO|Elo|UKD|Rating)", RegexOptions.IgnoreCase));
            if (iName < 0) continue;
            foreach (var tr in rows.Skip(h + 1))
            {
                var c = RowCells(tr);
                if (c.Count <= iName || string.IsNullOrWhiteSpace(c[iName])) continue;
                list.Add(new Player(
                    StartNo: iNo >= 0 && iNo < c.Count && int.TryParse(c[iNo], out var no) ? no : null,
                    Name: c[iName],
                    Rating: iRtg >= 0 && iRtg < c.Count ? ParseRating(c[iRtg]) : null));
            }
            if (list.Count > 0) break;
        }
        return list;
    }

    /// <summary>Tur tarihleri sayfası (art=14): tur no, tarih ("2026/10/03"), saat ("10.00").</summary>
    public static List<(int Round, DateTime? Date, string Time)> ParseSchedule(string html)
    {
        var doc = Parser.ParseDocument(html);
        var list = new List<(int, DateTime?, string)>();
        foreach (var tr in doc.QuerySelectorAll("tr.CRng1, tr.CRng2"))
        {
            var cells = RowCells(tr);
            if (cells.Count < 3 || !int.TryParse(cells[0], out var rd)) continue;
            var days = TournamentDates.Parse(cells[1]);
            list.Add((rd, days.Count > 0 ? days[0] : null, cells[2]));
        }
        return list;
    }

    // ---- kategori navigasyonu ----
    private static IReadOnlyList<CategoryRef> ParseCategories(IDocument doc, int tnr, string eventName)
    {
        var cats = new List<CategoryRef>();

        // "Turnuva seçimi" etiketli hücrenin yanındaki kap.
        var label = doc.QuerySelectorAll("td")
            .FirstOrDefault(td => Clean(td.TextContent).StartsWith("Turnuva seçimi", StringComparison.OrdinalIgnoreCase));
        var container = label?.NextElementSibling;

        if (container is not null)
        {
            foreach (var node in container.Children)
            {
                if (node.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
                {
                    var href = node.GetAttribute("href") ?? "";
                    var m = Regex.Match(href, @"[Tt]nr(\d+)\.aspx", RegexOptions.IgnoreCase);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var t))
                        cats.Add(new CategoryRef(t, Clean(node.TextContent), IsCurrent: false));
                }
                else
                {
                    // Mevcut grup: link olmayan (bold/italik) metin.
                    var txt = Clean(node.TextContent);
                    if (txt.Length > 0 && node.QuerySelector("a") is null)
                        cats.Add(new CategoryRef(tnr, txt, IsCurrent: true));
                }
            }
        }

        // Hiç kategori bulunamadıysa (tek gruplu turnuva) mevcut etkinliği ekle. Ad olarak
        // turnuva adının tamamı değil yalnızca kategori eki ("… AÇIK KATEGORİSİ" → "AÇIK") kullanılır;
        // ek yoksa boş kalır (kağıttaki Kategori kutusuna turnuva adı basılmasın).
        if (cats.Count == 0)
            cats.Add(new CategoryRef(tnr, EventGrouping.CategoryFromEventName(eventName), IsCurrent: true));

        // Mevcut işaretli yoksa, tnr eşleşeni mevcut say.
        if (!cats.Any(c => c.IsCurrent))
            cats = cats.Select(c => c.Tnr == tnr ? c with { IsCurrent = true } : c).ToList();

        return cats;
    }

    /// <summary>Tur sayısı bilinmediğinde gösterilecek makul üst sınır.</summary>
    public const int UnknownMaxRound = 11;

    /// <summary>
    /// Toplam tur sayısı ve eşlenmiş son tur. chess-results menüsünde eşlenmiş turlar
    /// "Tur1, Tur2, Tur3/7" diye link olarak listelenir; "/7" son eşlenmiş turun yanındadır.
    /// Sayfadaki program metni ("6. Tur 16.30") tur sayısı sanılmasın diye yalnızca bu
    /// bitişik yazımlı ("Tur3") link metinlerine bakılır.
    /// </summary>
    private static (int Max, int Current) ParseRounds(IDocument doc)
    {
        int max = 0, current = 0;
        foreach (var a in doc.QuerySelectorAll("a[href]"))
        {
            var m = Regex.Match(Clean(a.TextContent), @"^(?:Tur|Rd\.|Rnd\.?|Round)\s?(\d{1,2})(?:\s*/\s*(\d{1,2}))?$",
                RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            int r = int.Parse(m.Groups[1].Value);
            current = Math.Max(current, r);
            max = Math.Max(max, r);
            if (m.Groups[2].Success) max = Math.Max(max, int.Parse(m.Groups[2].Value));
        }

        // Görüntülenen tur link olmayabilir; "Tur5/5" düz metin olarak da geçer.
        var text = doc.Body?.TextContent ?? "";
        foreach (Match m in Regex.Matches(text, @"\bTur(\d{1,2})/(\d{1,2})\b"))
        {
            current = Math.Max(current, int.Parse(m.Groups[1].Value));
            max = Math.Max(max, int.Parse(m.Groups[2].Value));
        }

        return (max == 0 ? UnknownMaxRound : max, current);
    }

    // ---- yardımcılar ----
    private static string? InfoField(IDocument doc, params string[] labels)
    {
        foreach (var td in doc.QuerySelectorAll("td"))
        {
            var t = Clean(td.TextContent);
            foreach (var lbl in labels)
                if (t.Equals(lbl, StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith(lbl + ":", StringComparison.OrdinalIgnoreCase))
                {
                    var val = Clean(td.NextElementSibling?.TextContent ?? "");
                    if (val.Length > 0) return val;
                }
        }
        return null;
    }

    private static List<string> FindHeaderCells(IElement table)
    {
        foreach (var tr in table.QuerySelectorAll("tr"))
        {
            var cells = RowCells(tr);
            var joined = string.Join("|", cells).ToLowerInvariant();
            if (joined.Contains("beyaz") || joined.Contains("white") ||
                joined.Contains("masa") || joined.Contains("bo.") || joined.Contains("sonuç"))
                return cells;
        }
        return new List<string>();
    }

    private static List<string> RowCells(IElement row) =>
        row.Children
           .Where(c => c.TagName is "TD" or "TH")
           .Select(c => Clean(c.TextContent))
           .ToList();

    private static string Clean(string? s)
        => string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, @"\s+", " ").Trim();
}
