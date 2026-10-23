using NotasyonOtomasyonu.App.Lists;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Pano listeleri: her kategorinin son turunun EŞLEŞTİRME listesi ve sonucu girilmiş son turun
/// SONUÇ + SIRALAMA listesi (A4, salon panosuna asılacak), tek tıkla. Hızlı erişimin altında
/// küçük iki satır: "Eşleştirme: [Açık · T5] … [Tümü]" ve "Sonuç: [Açık · T4] … [Tümü]".
/// Sağdaki listenin altındaki "📋 Liste" menüsü de ekranda seçili kategori/turun listesini basar.
/// </summary>
public partial class MainForm
{
    private GroupBox? _grpLists;
    private FlowLayoutPanel? _flowPairLists, _flowResultLists;
    private Label? _lblPairLists, _lblResultLists, _lblArbiter;
    private Button? _btnArbiter;
    private Button? _btnGridList;
    private readonly ContextMenuStrip _gridListMenu = new();
    private readonly ToolTip _listTip = new();

    /// <summary>Kategori → sıralaması yayımlanmış (sonucu girilmiş) son tur; chess-results "5. Turdan Sonra Sıralama".</summary>
    private readonly Dictionary<int, int> _ranked = new();
    private readonly Dictionary<int, List<(int Round, DateTime? Date, string Time)>> _scheduleCache = new();

    private void InitListButtons()
    {
        _grpLists = new GroupBox
        {
            Text = "📋 Listeler — A4 (pano: eşleştirme · sonuç ve sıralama; hakem kağıtları)",
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Location = new Point(grpQuick.Left, grpQuick.Bottom + 6),
            Size = new Size(grpQuick.Width, 76),
        };
        _lblPairLists = SmallLabel("Eşleştirme");
        _lblResultLists = SmallLabel("Sonuç");
        _flowPairLists = new FlowLayoutPanel { WrapContents = true, Location = new Point(92, 20), Size = new Size(grpQuick.Width - 102, 28) };
        _flowResultLists = new FlowLayoutPanel { WrapContents = true, Location = new Point(92, 50), Size = new Size(grpQuick.Width - 102, 28) };
        _lblArbiter = SmallLabel("Hakem");
        _btnArbiter = new Button { Text = "🧑‍⚖️ Hakem eşleştirme kağıtları…", AutoSize = true, BackColor = Color.White, Font = new Font("Segoe UI", 8.5f) };
        StyleButton(_btnArbiter);
        _btnArbiter.Click += (_, _) => OpenArbiterSheets();
        _listTip.SetToolTip(_btnArbiter, "Tur başında hakemlere verilen eşleştirme kağıtları (Ctrl+H): iri yazı, her masada sonuç kutusu; " +
                                         "kategoriler ayrı sayfaya, sığdıkça aynı sayfaya ya da seçtiğiniz gruplara göre.");
        _grpLists.Controls.AddRange(new Control[] { _lblPairLists, _flowPairLists, _lblResultLists, _flowResultLists, _lblArbiter, _btnArbiter });
        Controls.Add(_grpLists);

        // Sağdaki listenin altında: ekranda seçili kategori + tur için liste.
        _btnGridList = new Button
        {
            Text = "📋 Liste ▾",
            Size = new Size(96, btnSelectNone.Height),
            Location = new Point(btnSelectNone.Right + 4, btnSelectNone.Top),
            Anchor = btnSelectNone.Anchor,
            BackColor = Color.White
        };
        StyleButton(_btnGridList);
        grpPairings.Controls.Add(_btnGridList);
        lblSelection.Left += _btnGridList.Width + 4;
        lblSelection.Width -= _btnGridList.Width + 4;
        _gridListMenu.Items.Add("📋 Bu turun eşleştirme listesi", null, async (_, _) => await PrintGridListAsync(results: false));
        _gridListMenu.Items.Add("🏁 Bu turun sonuçları ve sıralama", null, async (_, _) => await PrintGridListAsync(results: true));
        _gridListMenu.Items.Add(new ToolStripSeparator());
        _gridListMenu.Items.Add("🧑‍⚖️ Hakem eşleştirme kağıtları…", null, (_, _) => OpenArbiterSheets());
        _btnGridList.Click += (_, _) => _gridListMenu.Show(_btnGridList, new Point(0, _btnGridList.Height));
        _listTip.SetToolTip(_btnGridList, "Listede görünen kategori ve turun eşleştirme ya da sonuç listesini A4 olarak bas (salon panosu için).");

        foreach (var name in new[] { "colWIl", "colBIl" })
        {
            dgvPairings.Columns[name]!.DefaultCellStyle.ForeColor = Color.DimGray;
            dgvPairings.Columns[name]!.ToolTipText = "TSF kayıt listesinden kendiliğinden gelir. Düzeltmek için çift tıklayın.";
        }
    }

    private Label SmallLabel(string text) => new()
    {
        Text = text, AutoSize = false, Size = new Size(80, 26), TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = ThGreenDark, Font = new Font("Segoe UI", 9f, FontStyle.Bold)
    };

    private Button SmallButton(string text, object? tag, EventHandler click, bool strong = false)
    {
        var b = new Button
        {
            Text = text, Tag = tag, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 26), Margin = new Padding(1, 2, 1, 2), Padding = new Padding(2, 0, 2, 0),
            BackColor = Color.White, ForeColor = strong ? ThGreenDark : Color.FromArgb(40, 40, 40),
            Font = new Font("Segoe UI", 9f, strong ? FontStyle.Bold : FontStyle.Regular)
        };
        StyleButton(b);
        b.Click += click;
        return b;
    }

    /// <summary>Kategoriler yüklenince küçük liste düğmelerini kurar (BuildQuickButtons'tan).</summary>
    private void BuildListButtons(IReadOnlyList<CategoryRef> cats)
    {
        if (_grpLists is null || _flowPairLists is null || _flowResultLists is null) return;
        _flowPairLists.Controls.Clear();
        _flowResultLists.Controls.Clear();
        foreach (var c in cats)
        {
            _flowPairLists.Controls.Add(SmallButton("", c, async (_, _) => await PrintListsAsync(new[] { c }, results: false)));
            _flowResultLists.Controls.Add(SmallButton("", c, async (_, _) => await PrintListsAsync(new[] { c }, results: true)));
            UpdateListButtons(c);
        }
        // "Tümü" düğmeleri sağda ayrı sütunda (hızlı erişimdeki "Tüm Kategoriler" gibi); tek kategoride gerek yok.
        if (_btnAllPairLists is null)
        {
            _btnAllPairLists = SmallButton("★ Tümü", null, async (_, _) => await PrintListsAsync(CurrentCategories(), results: false), strong: true);
            _btnAllResultLists = SmallButton("★ Tümü", null, async (_, _) => await PrintListsAsync(CurrentCategories(), results: true), strong: true);
            _listTip.SetToolTip(_btnAllPairLists, "Tüm kategorilerin son eşlenen turunun eşleştirme listesi (her kategori ayrı sayfa).");
            _listTip.SetToolTip(_btnAllResultLists, "Tüm kategorilerin sonucu girilmiş son turunun sonuçları ve sıralaması (her kategori ayrı sayfa).");
            _grpLists.Controls.Add(_btnAllPairLists);
            _grpLists.Controls.Add(_btnAllResultLists);
        }
        _btnAllPairLists.Visible = _btnAllResultLists!.Visible = cats.Count > 1;
    }

    private Button? _btnAllPairLists, _btnAllResultLists;

    /// <summary>Kategorinin liste düğmelerinin yazısı: "Açık · T5" (eşleştirme) ve "Açık · T4" (sonuç).</summary>
    private void UpdateListButtons(CategoryRef c)
    {
        if (_flowPairLists is null || _flowResultLists is null) return;
        var name = CompactName(c);
        bool known = _roundCache.TryGetValue(c.Tnr, out var r);
        foreach (var b in _flowPairLists.Controls.OfType<Button>().Where(x => x.Tag is CategoryRef k && k.Tnr == c.Tnr))
        {
            b.Text = !known ? $"{name} …" : r.Current > 0 ? $"{name} T{r.Current}" : $"{name} —";
            b.Enabled = !known || r.Current > 0;
            _listTip.SetToolTip(b, known && r.Current > 0
                ? $"{c.Name} — {r.Current}. tur eşleştirme listesini A4 bas (masa, oyuncular, puanlar)."
                : $"{c.Name} — henüz eşleştirme yok.");
        }
        bool rk = _ranked.TryGetValue(c.Tnr, out var ranked);
        foreach (var b in _flowResultLists.Controls.OfType<Button>().Where(x => x.Tag is CategoryRef k && k.Tnr == c.Tnr))
        {
            b.Text = !rk ? $"{name} …" : ranked > 0 ? $"{name} T{ranked}" : $"{name} —";
            b.Enabled = !rk || ranked > 0;
            _listTip.SetToolTip(b, rk && ranked > 0
                ? $"{c.Name} — {ranked}. tur sonuçları ve {ranked}. turdan sonra sıralama (A4)."
                : $"{c.Name} — henüz sonuç girilmemiş.");
        }
    }

    /// <summary>Küçük düğme için kısa ad: "12 Yaş ve Altı Kategorisi" → "12 Yaş".</summary>
    private static string CompactName(CategoryRef c)
        => System.Text.RegularExpressions.Regex.Replace(ButtonText(c), @"\s+ve\s+(Altı|Alti|ALTI|Üstü|Ustu|ÜSTÜ)\b", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    /// <summary>Liste grubunun iç yerleşimi; yüksekliği içeriğe göre (RelayoutLeft'ten).</summary>
    private void LayoutListGroup()
    {
        if (_grpLists is null || _flowPairLists is null || _flowResultLists is null || _lblPairLists is null || _lblResultLists is null) return;
        int pad = flowQuick.Left, top = flowQuick.Top;
        int labelW = Math.Max(TextRenderer.MeasureText("Eşleştirme", _lblPairLists.Font).Width, TextRenderer.MeasureText("Sonuç", _lblPairLists.Font).Width) + 8;
        bool all = _btnAllPairLists is { Visible: true } && _btnAllResultLists is not null;
        int allW = all ? Math.Max(_btnAllPairLists!.PreferredSize.Width, 64) : 0;
        int flowLeft = pad + labelW, flowW = _grpLists.ClientSize.Width - flowLeft - pad - (all ? allW + 4 : 0);
        _lblPairLists.SetBounds(pad, top, labelW, _lblPairLists.Height);
        _flowPairLists.SetBounds(flowLeft, top, flowW, _flowPairLists.Height);
        FitFlow(_flowPairLists);
        _flowResultLists.SetBounds(flowLeft, _flowPairLists.Bottom + 2, flowW, _flowResultLists.Height);
        FitFlow(_flowResultLists);
        _lblResultLists.SetBounds(pad, _flowResultLists.Top, labelW, _lblResultLists.Height);
        if (all)
        {
            int x = _grpLists.ClientSize.Width - pad - allW;
            _btnAllPairLists!.SetBounds(x, _flowPairLists.Top + 2, allW, _btnAllPairLists.PreferredSize.Height);
            _btnAllResultLists!.SetBounds(x, _flowResultLists.Top + 2, allW, _btnAllResultLists.PreferredSize.Height);
        }
        int bottom = _flowResultLists.Bottom;
        if (_lblArbiter is not null && _btnArbiter is not null)
        {
            _btnArbiter.Location = new Point(flowLeft + 2, _flowResultLists.Bottom + 4);
            _lblArbiter.SetBounds(pad, _btnArbiter.Top + (_btnArbiter.Height - _lblArbiter.Height) / 2, labelW, _lblArbiter.Height);
            bottom = _btnArbiter.Bottom + 2;
        }
        _grpLists.Height = bottom + _quickPadBottom;
    }

    // ================= Liste baskısı =================
    /// <summary>
    /// Kategorilerin listelerini basar: eşleştirme = son eşlenen tur (ya da <paramref name="forceRound"/>),
    /// sonuç = sonucu girilmiş son tur + o turdan sonraki sıralama. Her kategori yeni sayfada.
    /// </summary>
    private async Task PrintListsAsync(IReadOnlyList<CategoryRef> cats, bool results, int? forceRound = null)
    {
        if (cats.Count == 0) { SetStatus("Önce bir turnuva yükleyin.", ok: false); return; }
        SetBusy(true);
        try
        {
            UpdateConfigFromUi();
            var eventName = EventGrouping.BaseName(string.IsNullOrWhiteSpace(_config.Tournament.Name) ? CurrentEventName() : _config.Tournament.Name);
            var blocks = new List<ListBlock>();
            var skipped = new List<string>();
            foreach (var cat in cats)
            {
                if (cat.Tnr < 0) { skipped.Add(cat.Name); continue; } // kayıttaki turnuva: henüz eşleştirme yok
                SetStatus($"Liste hazırlanıyor: {cat.Name}…", ok: true);
                var r = await RoundsOfAsync(cat.Tnr);
                if (results && forceRound is null && !_ranked.ContainsKey(cat.Tnr))
                {
                    var info = await _online.GetEventAsync(cat.Tnr);
                    _ranked[cat.Tnr] = info.RankedRound;
                }
                UpdateListButtons(cat);
                int round = forceRound ?? (results ? _ranked.GetValueOrDefault(cat.Tnr) : r.Current);
                if (round <= 0) { skipped.Add(cat.Name); continue; }

                Tournament t;
                try { t = await _online.GetPairingsAsync(cat.Tnr, round, r.System); }
                catch (InvalidOperationException) { skipped.Add(cat.Name); continue; }

                var sections = new List<ListSection> { ListPrinter.RoundSection(t, round, results, await RoundWhenAsync(cat.Tnr, round)) };
                if (results)
                {
                    try
                    {
                        if (await _online.GetStandingsAsync(cat.Tnr, round) is { Rows.Count: > 0 } st)
                            sections.Add(ListPrinter.StandingsSection(st));
                    }
                    catch (Exception ex) { Log($"• {cat.Name}: sıralama alınamadı ({FriendlyNet(ex)})."); }
                }
                blocks.Add(new ListBlock(eventName, cat.Name, sections));
            }

            if (blocks.Count == 0)
            {
                SetStatus(results ? "Henüz sonucu girilmiş tur yok." : "Henüz eşleştirme yayımlanmamış.", ok: false);
                return;
            }
            if (skipped.Count > 0) Log($"• Atlandı ({(results ? "sonuç yok" : "eşleştirme yok")}): {string.Join(", ", skipped)}");
            await ShowListAsync(blocks, results);
        }
        catch (Exception ex) { Fail("Liste yazdırılamadı", FriendlyNet(ex)); }
        finally { SetBusy(false); }
    }

    /// <summary>Sağdaki listede görünen kategori/turun listesi (dosyadan gelen eşleştirme de olabilir).</summary>
    private async Task PrintGridListAsync(bool results)
    {
        if (_gridTournament is null) { SetStatus("Önce eşleştirmeyi listeye getirin.", ok: false); return; }
        if (rbOnline.Checked && SelectedCategory() is { } cat)
        {
            await PrintListsAsync(new[] { cat }, results, _gridTournament.RoundNo);
            return;
        }
        // Dosya: elimizdeki eşleştirmeden (sıralama yok).
        var t = ApplyConfigMeta(_gridTournament with { RoundNo = (int)numRound.Value });
        var block = new ListBlock(t.Name, t.Pairings.FirstOrDefault()?.Category ?? "", new[] { ListPrinter.RoundSection(t, t.RoundNo, results, null) });
        await ShowListAsync(new[] { block }, results);
    }

    private Task ShowListAsync(IReadOnlyList<ListBlock> blocks, bool results)
    {
        var kind = results ? "sonuç listesi" : "eşleştirme listesi";
        var printer = new ListPrinter(blocks, $"{blocks[0].EventName} - {kind}");
        bool printed = printer.PrintWithPreview(this);
        var what = string.Join(", ", blocks.Select(b => b.Category));
        if (printed)
        {
            SetStatus($"✓ {char.ToUpper(kind[0]) + kind[1..]} yazıcıya gönderildi ({printer.PageCount} sayfa).", ok: true);
            Log($"📋 Basıldı: {kind} — {what} ({printer.PageCount} sayfa).");
        }
        else SetStatus("Önizleme kapatıldı; liste basılmadı.", ok: true);
        return Task.CompletedTask;
    }

    // ================= Hakem eşleştirme kağıtları =================
    private void OpenArbiterSheets()
    {
        if (BlockedForTsfEvent("Hakem eşleştirme kağıtları")) return;
        var cats = ToolCategories();
        if (cats is null) return;
        UpdateConfigFromUi();
        var eventName = EventGrouping.BaseName(string.IsNullOrWhiteSpace(_config.Tournament.Name) ? CurrentEventName() : _config.Tournament.Name);
        var ctx = new ArbiterSheetsContext(eventName, cats.Where(c => c.Tnr > 0).ToList(), LoadArbiterSheetAsync,
                                           _config.ArbiterSheets, () => _config.Save(_configPath), _outputDir);
        using var f = new ArbiterSheetsForm(ctx) { Icon = Icon };
        f.ShowDialog(this);
    }

    /// <summary>Kategorinin hakem kağıdı: verilen tur (yoksa son eşlenen tur); eşleştirme yoksa null.</summary>
    private async Task<ArbiterSheet?> LoadArbiterSheetAsync(CategoryRef cat, int? round)
    {
        var r = await RoundsOfAsync(cat.Tnr);
        UpdateListButtons(cat);
        int rd = round ?? r.Current;
        if (rd <= 0) return null;
        Tournament t;
        try { t = await _online.GetPairingsAsync(cat.Tnr, rd, r.System); }
        catch (InvalidOperationException) { return null; }
        return t.Pairings.Count == 0 ? null : new ArbiterSheet(cat.Name, rd, await RoundWhenAsync(cat.Tnr, rd), t);
    }

    /// <summary>Turun tarihi ve saati ("11.10.2026 • 10.00"); program yoksa null.</summary>
    private async Task<string?> RoundWhenAsync(int catTnr, int round)
    {
        try
        {
            if (!_scheduleCache.TryGetValue(catTnr, out var s))
                _scheduleCache[catTnr] = s = await _online.GetScheduleAsync(catTnr);
            var hit = s.FirstOrDefault(x => x.Round == round);
            if (hit.Round == 0) return null;
            var parts = new[] { hit.Date?.ToString("dd.MM.yyyy"), hit.Time }.Where(x => !string.IsNullOrWhiteSpace(x));
            var text = string.Join(" • ", parts);
            return text.Length == 0 ? null : text;
        }
        catch { return null; }
    }
}
