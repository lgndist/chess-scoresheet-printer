using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Registration;

/// <summary>Kayıt listesi penceresinin ana pencereden aldığı bağlam.</summary>
public sealed record RegistrationContext(
    ChessResultsService Online,
    int EventTnr,
    string EventName,
    IReadOnlyList<CategoryRef> Categories,
    string? Province,
    AppConfig Config,
    Action SaveConfig);

/// <summary>Kayıt formu seçeneği: sitedeki form, bu turnuvaya benzerliği ve (alındıysa) sporcu listesi.</summary>
internal sealed class OptionItem
{
    public required RegistrationOption Option { get; init; }
    public double TitleScore { get; set; }
    public int? Matched { get; set; }
    public int? Entries { get; set; }
    public RegistrationPage? Page { get; set; }
    public string? Error { get; set; }
    public bool Likely { get; set; }

    public override string ToString()
    {
        var info = Error is not null ? $"  — {Error}"
            : Matched is int m && Entries is int n ? $"  — {m}/{n} sporcu bu turnuvada" + (Likely ? " ✓" : "")
            : Entries is int e ? $"  — {e} kayıt" : "";
        return (Page is { Closed: true } ? "[kayıt bitti] " : "") + Option.Title + info;
    }
}

/// <summary>
/// TSF il sitesindeki turnuva kayıt listesini turnuva başında BİR KEZ alıp saklar, chess-results
/// sporcularıyla eşler ve sonucu gösterir. Benzer/belirsiz/bulunamayan sporcular işaretlenir,
/// il elle düzeltilebilir. Sonuç notasyondaki "İl" kutusuna basılır.
/// </summary>
public sealed class RegistrationForm : Form
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color Border = Color.FromArgb(208, 208, 198);
    private static readonly Color Yellow = Color.FromArgb(255, 246, 204);
    private static readonly Color Orange = Color.FromArgb(255, 228, 196);
    private static readonly Color Grey = Color.FromArgb(240, 240, 236);

    private readonly RegistrationContext _ctx;
    private AppConfig Cfg => _ctx.Config;
    private string Key => _ctx.EventTnr.ToString();

    private readonly TextBox _txtSite = new() { Width = 300 };
    private readonly Button _btnList = new() { Text = "Kayıttaki turnuvaları getir", AutoSize = true, Height = 30 };
    private readonly CheckedListBox _lstOptions = new() { Width = 640, Height = 96, CheckOnClick = true, IntegralHeight = false };
    private readonly TextBox _txtTid = new() { Width = 300, PlaceholderText = "Kayıt linki ya da numarası (tid)" };
    private readonly Button _btnAddTid = new() { Text = "Ekle", AutoSize = true, Height = 30 };
    private readonly Button _btnFetch = new() { Text = "⬇ Kayıt listesini al ve kaydet", AutoSize = true, Height = 34, BackColor = Green, ForeColor = Color.White };
    private readonly Label _lblSnapshot = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(700, 0) };
    private readonly Label _lblEvent = new() { AutoSize = true, ForeColor = Color.FromArgb(40, 40, 40), MaximumSize = new Size(1040, 0), Margin = new Padding(0, 0, 0, 2) };
    private readonly DataGridView _grid = new();
    private readonly CheckBox _chkOnlyIssues = new() { Text = "Yalnız kontrol gerekenler", AutoSize = true };
    private readonly Label _lblSummary = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button _btnSave = new() { Text = "💾 Kaydet ve kapat", Width = 180, Height = 40, BackColor = Green, ForeColor = Color.White };
    private readonly Button _btnClose = new() { Text = "Kapat", Width = 100, Height = 40 };

    private List<RosterPlayer> _roster = new();
    private readonly Dictionary<string, string> _manual = new(); // bu oturumda elle değişenler
    private bool _loading, _dirty;

    private static readonly string[] ProvinceItems =
        new[] { "" }.Concat(Provinces.List.Where(p => p != Provinces.All).Select(p => Provinces.Print(p))).ToArray();

    public RegistrationForm(RegistrationContext ctx)
    {
        _ctx = ctx;
        Text = "Kayıt Listesi ve İller — " + EventGrouping.BaseName(ctx.EventName);
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 760);
        MinimumSize = new Size(900, 600);
        this.FitToScreen(); // küçük ekranda / yüksek ölçekte taşmasın
        BackColor = Color.FromArgb(250, 250, 247);
        BuildLayout();

        _txtSite.Text = TsfSites.SiteFor(ctx.Province, Cfg.TsfSites) ?? "";
        _btnList.Click += async (_, _) => await LoadOptionsAsync(autoCheck: true);
        _btnAddTid.Click += (_, _) => AddTid();
        _btnFetch.Click += async (_, _) => await FetchAsync();
        _chkOnlyIssues.CheckedChanged += (_, _) => ApplyFilter();
        _btnSave.Click += (_, _) => { Save(); DialogResult = DialogResult.OK; Close(); };
        _btnClose.Click += (_, _) => Close();
        FormClosing += (_, e) =>
        {
            if (!_dirty || DialogResult == DialogResult.OK) return;
            var r = MessageBox.Show(this, "İllerde yaptığınız değişiklikler kaydedilsin mi?", "Kayıt listesi",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) e.Cancel = true;
            else if (r == DialogResult.Yes) Save();
        };
        Shown += async (_, _) => await StartAsync();
    }

    // ================= Yerleşim =================
    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14, 10, 14, 4) };
        // Liste hangi turnuvaya bağlanacak: ana ekranda açık olan turnuva (yanlış turnuva açıkken alınmasın)
        _lblEvent.Text = $"Turnuva: {_ctx.EventName}  (chess-results #{_ctx.EventTnr})";
        _lblEvent.Font = new Font(Font, FontStyle.Bold);
        top.Controls.Add(_lblEvent);
        top.Controls.Add(Header("1. TSF il sitesi"));
        var siteRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        siteRow.Controls.AddRange(new Control[] { _txtSite, _btnList });
        top.Controls.Add(siteRow);
        top.Controls.Add(Header("2. Bu turnuvanın kayıt formları (sporcuları chess-results listesiyle eşleşenler işaretlenir; her kategori ayrı form olabilir)"));
        var optRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var tidCol = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
        var tidRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        tidRow.Controls.AddRange(new Control[] { _txtTid, _btnAddTid });
        tidCol.Controls.Add(tidRow);
        tidCol.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = Color.DimGray, Margin = new Padding(4, 6, 0, 0),
            Text = "Listede yoksa sitedeki \"Kayıt için tıklayınız\" linkini buraya yapıştırın."
        });
        optRow.Controls.AddRange(new Control[] { _lstOptions, tidCol });
        top.Controls.Add(optRow);
        var fetchRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        _lblSnapshot.Margin = new Padding(10, 9, 0, 0);
        fetchRow.Controls.AddRange(new Control[] { _btnFetch, _lblSnapshot });
        top.Controls.Add(fetchRow);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.RowTemplate.Height = 28;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kat", HeaderText = "Kategori", Width = 130, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ad", HeaderText = "Sporcu (chess-results)", Width = 250, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Puan", HeaderText = "Puan", Width = 60, ReadOnly = true });
        var il = new DataGridViewComboBoxColumn { Name = "Il", HeaderText = "İl", Width = 160, FlatStyle = FlatStyle.Flat };
        il.Items.AddRange(ProvinceItems);
        _grid.Columns.Add(il);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Durum", HeaderText = "Durum", Width = 170, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Not", HeaderText = "Kayıttaki karşılığı / not", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (_loading || e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Il") return;
            var row = _grid.Rows[e.RowIndex];
            _manual[(string)row.Tag!] = row.Cells["Il"].Value?.ToString() ?? "";
            _dirty = true;
            SetStatus(row, "Elle", null);
            UpdateSummary();
        };
        _grid.DataError += (_, e) => e.ThrowException = false;

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 6) };
        gridPanel.Controls.Add(_grid);
        gridPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Top, Height = 40, ForeColor = Color.DimGray,
            Text = "İller notasyondaki \"İl / City\" kutusuna basılır. Sarı: benzer ad (ikinci ad eksik ya da tek harf farkı) — kontrol edin. " +
                   "Turuncu: aynı adla farklı illerden kayıt var. Gri: kayıtta bulunamadı. İli tablodan değiştirebilirsiniz."
        });

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(12), BackColor = Color.FromArgb(242, 242, 236) };
        var bl = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        _chkOnlyIssues.Margin = new Padding(4, 11, 0, 0);
        _lblSummary.Margin = new Padding(14, 11, 0, 0);
        bl.Controls.AddRange(new Control[] { _chkOnlyIssues, _lblSummary });
        var br = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        br.Controls.AddRange(new Control[] { _btnClose, _btnSave });
        bottom.Controls.Add(bl);
        bottom.Controls.Add(br);
        foreach (var b in new[] { _btnList, _btnAddTid, _btnFetch, _btnSave, _btnClose }) Style(b);
        _btnSave.Font = _btnFetch.Font = new Font("Segoe UI", 10f, FontStyle.Bold);

        Controls.Add(gridPanel);
        Controls.Add(top);
        Controls.Add(bottom);
    }

    private Label Header(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = GreenDark, Margin = new Padding(0, 6, 0, 4)
    };

    private static void Style(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.Cursor = Cursors.Hand;
        b.Margin = new Padding(4, 0, 4, 2);
        bool primary = b.BackColor == Green;
        if (!primary) b.BackColor = Color.White;
        b.FlatAppearance.BorderColor = primary ? GreenDark : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(134, 168, 100) : Color.FromArgb(236, 241, 231);
    }

    // ================= Akış =================
    private async Task StartAsync()
    {
        var reg = ProvinceBook.For(Cfg, _ctx.EventTnr);
        if (reg is not { Entries.Count: > 0 } && ProvinceBook.ByTitle(Cfg, _ctx.EventName) is { } found)
        {
            // Liste başka bir turnuva açıkken alınmış: başlığı bu turnuvaya uyuyor → bu turnuvaya da bağla.
            reg = Attach(found, keepOverrides: reg);
            _ctx.SaveConfig();
        }
        ShowSnapshot(reg);
        await LoadRosterAsync();
        if (reg is { Tids.Count: > 0 })
        {
            // Kayıtlı liste var: siteye tekrar gidilmez; formlar listede işaretli görünür.
            for (int i = 0; i < reg.Tids.Count; i++)
            {
                var tid = reg.Tids[i];
                var entries = reg.Entries.Where(e => e.Tid == tid).ToList();
                AddOption(new OptionItem
                {
                    Option = new RegistrationOption(tid, i < reg.Titles.Count ? reg.Titles[i] : $"Form {tid}"),
                    Entries = entries.Count,
                    Matched = _roster.Count > 0 ? RegistrationMatch.Overlap(_roster, entries) : null,
                    Likely = true
                }, true);
            }
        }
        else if (_txtSite.Text.Length > 0) await LoadOptionsAsync(autoCheck: true);
        FillGrid();
    }

    /// <summary>Başka turnuvaya bağlı listeyi bu turnuvaya kopyalar (bu turnuvada elle verilen iller korunur).</summary>
    private EventRegistration Attach(EventRegistration source, EventRegistration? keepOverrides)
    {
        var copy = new EventRegistration
        {
            Site = source.Site, EventName = _ctx.EventName, Tids = source.Tids.ToList(), Titles = source.Titles.ToList(),
            FetchedAt = source.FetchedAt, Entries = source.Entries,
            Overrides = keepOverrides?.Overrides ?? new()
        };
        Cfg.Registrations[Key] = copy;
        _attachedFrom = source.EventName.Length > 0 ? source.EventName : string.Join(", ", source.Titles);
        return copy;
    }

    private string? _attachedFrom;

    private async Task LoadRosterAsync()
    {
        _lblSummary.Text = "Sporcular chess-results'tan alınıyor…";
        var list = new List<RosterPlayer>();
        var failed = new List<string>();
        foreach (var c in _ctx.Categories)
        {
            try
            {
                var players = await _ctx.Online.GetStartingListAsync(c.Tnr);
                list.AddRange(players.Select(p => new RosterPlayer(p.Name, p.Rating, c.Name)));
            }
            catch { failed.Add(c.Name); }
        }
        _roster = list;
        if (failed.Count > 0) _lblSummary.Text = "Alınamayan kategori: " + string.Join(", ", failed);
    }

    /// <summary>
    /// Sitedeki kayda açık formları getirir ve her birini bu turnuvanın sporcularıyla karşılaştırır:
    /// kayıtlılarının çoğu chess-results listesinde olan formlar işaretlenir (ad benzerliği yanıltabilir,
    /// sporcular yanıltmaz). Sporcu listesi henüz yoksa ada bakılır.
    /// </summary>
    private async Task LoadOptionsAsync(bool autoCheck)
    {
        var site = _txtSite.Text.Trim().TrimEnd('/');
        if (site.Length == 0) { _lblSnapshot.Text = "TSF il sitesi bilinmiyor; kayıt linkini sağdaki kutuya yapıştırın."; return; }
        _btnList.Enabled = _btnFetch.Enabled = false;
        _lblSnapshot.Text = "Kayda açık turnuvalar alınıyor…";
        try
        {
            using var sites = new TsfSites();
            var opts = await sites.ListOpenAsync(site);
            var keep = _lstOptions.CheckedItems.Cast<OptionItem>().Select(o => o.Option.Tid).ToHashSet();
            var items = opts.Select(o => new OptionItem { Option = o, TitleScore = RegistrationMatch.TitleScore(o.Title, _ctx.EventName) }).ToList();
            // Kayıtları yeni bitmiş formlar sitedeki listede yok ama sayfaları okunuyor: onlar da seçilebilir.
            var known = opts.Select(o => o.Tid).Concat(_ctx.Config.Registrations.Values.Where(r => r.Site == site).SelectMany(r => r.Tids)).ToList();
            if (known.Count > 0)
            {
                _lblSnapshot.Text = "Kayıtları yeni bitmiş formlar aranıyor…";
                foreach (var p in await sites.ListRecentlyClosedAsync(site, known.Max(), opts.Select(o => o.Tid)))
                    items.Add(new OptionItem
                    {
                        Option = new RegistrationOption(p.Tid, p.Title), Page = p, Entries = p.Entries.Count,
                        TitleScore = RegistrationMatch.TitleScore(p.Title, _ctx.EventName)
                    });
            }
            if (_roster.Count > 0)
            {
                int i = 0;
                foreach (var it in items)
                {
                    if (it.Page is not null) { it.Matched = RegistrationMatch.Overlap(_roster, it.Page.Entries); i++; continue; }
                    _lblSnapshot.Text = $"Kayıt formları sporcularla karşılaştırılıyor ({++i}/{items.Count}): {it.Option.Title}…";
                    try
                    {
                        it.Page = await sites.GetPageAsync(site, it.Option.Tid);
                        if (it.Page is null) { it.Error = "form okunamadı"; continue; }
                        it.Entries = it.Page.Entries.Count;
                        it.Matched = RegistrationMatch.Overlap(_roster, it.Page.Entries);
                    }
                    catch (Exception ex) { it.Error = FriendlySiteError(ex); }
                }
            }
            foreach (var it in items)
                it.Likely = RegistrationMatch.IsLikely(it.TitleScore, it.Matched ?? 0, it.Entries ?? 0, it.Page is null ? 0 : _roster.Count);

            _lstOptions.Items.Clear();
            foreach (var it in items.OrderByDescending(x => x.Likely).ThenByDescending(x => x.Matched ?? -1).ThenByDescending(x => x.TitleScore))
                AddOption(it, keep.Contains(it.Option.Tid) || (autoCheck && it.Likely));

            int likely = items.Count(x => x.Likely);
            _lblSnapshot.Text = opts.Count == 0
                ? "Sitede şu an kayda açık turnuva yok. Kayıt kapandıysa liste alınamaz; linki biliyorsanız yapıştırıp deneyin."
                : likely > 0
                    ? $"{opts.Count} kayıt formundan {likely} tanesi bu turnuvaya ait görünüyor ve işaretlendi. Kontrol edip listeyi alın."
                    : $"{opts.Count} kayıt formu bulundu ama hiçbiri bu turnuvaya benzemiyor (sporcuları/adı uyuşmuyor). " +
                      "Turnuvanın formu kapanmış olabilir; doğru olduğundan eminseniz elle işaretleyin.";
        }
        catch (Exception ex) { _lblSnapshot.Text = FriendlySiteError(ex); }
        finally { _btnList.Enabled = _btnFetch.Enabled = true; }
    }

    /// <summary>Ağ hatasını kullanıcıya anlaşılır yazar (TSF siteleri zaman zaman erişilemez oluyor).</summary>
    private static string FriendlySiteError(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null && inner is not System.Net.Sockets.SocketException) inner = inner.InnerException;
        if (inner is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.HostNotFound or System.Net.Sockets.SocketError.NoData or System.Net.Sockets.SocketError.TryAgain })
            return "TSF sitesine ulaşılamıyor (alan adı bulunamadı): site şu an kapalı ya da internet bağlantısı yok. Daha önce alınmış liste varsa o kullanılır.";
        if (ex is TaskCanceledException or TimeoutException)
            return "TSF sitesi yanıt vermedi (zaman aşımı). Biraz sonra tekrar deneyin.";
        if (ex is HttpRequestException h)
            return "TSF sitesi okunamadı" + (h.StatusCode is { } c ? $" (HTTP {(int)c})" : "") + ": " + h.Message;
        return "TSF sitesi okunamadı: " + ex.Message;
    }

    private void AddOption(OptionItem o, bool check)
    {
        foreach (OptionItem x in _lstOptions.Items) if (x.Option.Tid == o.Option.Tid) return;
        int i = _lstOptions.Items.Add(o);
        _lstOptions.SetItemChecked(i, check);
    }

    private void AddTid()
    {
        var input = _txtTid.Text.Trim();
        var tid = TsfRegistrations.ParseTid(input);
        if (tid is null) { MessageBox.Show(this, "Linkte \"tid=…\" numarası bulunamadı.", "Kayıt linki", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (TsfRegistrations.SiteOf(input) is { } site) _txtSite.Text = site;
        AddOption(new OptionItem { Option = new RegistrationOption(tid.Value, $"Kayıt formu {tid}"), Likely = true }, true);
        _txtTid.Clear();
    }

    private async Task FetchAsync()
    {
        var site = _txtSite.Text.Trim().TrimEnd('/');
        var chosen = _lstOptions.CheckedItems.Cast<OptionItem>().ToList();
        if (site.Length == 0 || chosen.Count == 0)
        {
            MessageBox.Show(this, "Önce TSF il sitesini ve bu turnuvanın kayıt formlarını seçin.", "Kayıt listesi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        // Sporcuları bu turnuvayla uyuşmayan form işaretlendiyse (yanlış turnuva açıkken alınmasın) sor.
        var odd = chosen.Where(c => !c.Likely && c.Matched is not null).ToList();
        if (odd.Count > 0 && MessageBox.Show(this,
                "Seçilen kayıt formlarının sporcuları açık olan turnuvanın sporcularıyla uyuşmuyor:\n\n" +
                string.Join("\n", odd.Select(o => $"• {o.Option.Title} ({o.Matched}/{o.Entries} sporcu)")) +
                $"\n\nAçık turnuva: {_ctx.EventName}\n\nAna ekranda doğru turnuvanın açık olduğundan emin olun. Yine de bu turnuvaya bağlansın mı?",
                "Kayıt listesi", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        var old = ProvinceBook.For(Cfg, _ctx.EventTnr);
        if (old is { Entries.Count: > 0 } && MessageBox.Show(this,
                $"Bu turnuvanın kayıt listesi {old.FetchedAt:dd.MM.yyyy HH:mm} tarihinde alınmış ({old.Entries.Count} sporcu). Yeniden alınsın mı?\n" +
                "Elle verdiğiniz iller korunur.", "Kayıt listesi", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _btnFetch.Enabled = false;
        var problems = new List<string>();
        var reg = new EventRegistration { Site = site, EventName = _ctx.EventName, FetchedAt = DateTime.Now, Overrides = old?.Overrides ?? new() };
        try
        {
            using var sites = new TsfSites();
            foreach (var o in chosen)
            {
                _lblSnapshot.Text = $"Alınıyor: {o.Option.Title}…";
                var page = o.Page ?? await sites.GetPageAsync(site, o.Option.Tid);
                if (page is null) { problems.Add($"• {o.Option.Title}: form kapanmış ya da bu numaraya ait değil (site başka bir liste gösteriyor)."); continue; }
                if (page.Total is int tot && tot != page.Entries.Count) problems.Add($"• {page.Title}: sayfada {tot} kayıt yazıyor, {page.Entries.Count} satır okundu.");
                reg.Tids.Add(page.Tid);
                reg.Titles.Add(page.Title);
                reg.Entries.AddRange(page.Entries);
            }
        }
        catch (Exception ex) { problems.Add("• " + FriendlySiteError(ex)); }
        finally { _btnFetch.Enabled = true; }

        if (reg.Entries.Count == 0)
        {
            _lblSnapshot.Text = "Kayıt listesi alınamadı.";
            MessageBox.Show(this, "Kayıt listesi alınamadı.\n\n" + string.Join("\n", problems), "Kayıt listesi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowSnapshot(old);
            return;
        }
        Cfg.Registrations[Key] = reg;
        _attachedFrom = null;
        TrimOld();
        _ctx.SaveConfig();
        ShowSnapshot(reg);
        FillGrid();
        Save(); // eşleşmeler hemen kaydedilir; pencere kapanınca da baskıda kullanılır
        if (problems.Count > 0)
            MessageBox.Show(this, "Liste kaydedildi; dikkat:\n\n" + string.Join("\n", problems), "Kayıt listesi", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>En eski kayıt listelerini atar (config şişmesin): son 30 turnuva tutulur.</summary>
    private void TrimOld()
    {
        foreach (var k in Cfg.Registrations.OrderByDescending(x => x.Value.FetchedAt).Skip(30).Select(x => x.Key).ToList())
            Cfg.Registrations.Remove(k);
    }

    private void ShowSnapshot(EventRegistration? reg)
    {
        _lblSnapshot.Text = reg is not { Entries.Count: > 0 }
            ? "Bu turnuva için kayıt listesi henüz alınmadı. Turnuva başında bir kez almanız yeterli; kayıt kapanınca sitede liste kalmaz."
            : $"Kayıt listesi {reg.FetchedAt:dd.MM.yyyy HH:mm} tarihinde alındı: {reg.Entries.Count} sporcu, {reg.Tids.Count} form" +
              (string.Join(", ", reg.Titles) is { Length: > 0 } t ? $" ({t})" : "") + ". Tekrar alınmasına gerek yok." +
              $"\nKategoriler: {string.Join(" · ", RegistrationCategories.Counts(reg).Select(c => $"{c.Name} {c.Count}"))}" +
              (_attachedFrom is not null ? $"\nBu liste başka bir turnuva açıkken alınmıştı; başlığı bu turnuvaya uyduğu için bu turnuvaya da bağlandı." : "");
    }

    // ================= Tablo =================
    private void FillGrid()
    {
        _loading = true;
        _grid.Rows.Clear();
        var reg = ProvinceBook.For(Cfg, _ctx.EventTnr);
        var matches = reg is { Entries.Count: > 0 }
            ? ProvinceMatcher.MatchAll(_roster, reg.Entries)
            : _roster.Select(p => new ProvinceMatch(p, MatchKind.NotFound, null, 0)).ToList();

        foreach (var m in matches)
        {
            var key = ProvinceMatcher.PersonKey(m.Player.Name);
            int i = _grid.Rows.Add(m.Player.Category, m.Player.Name, m.Player.Rating?.ToString() ?? "", "", "", "");
            var row = _grid.Rows[i];
            row.Tag = key;

            string? manual = _manual.TryGetValue(key, out var mm) ? mm : reg?.Overrides.GetValueOrDefault(key);
            if (manual is not null) { SetIl(row, manual); SetStatus(row, "Elle", m.Entry is null ? null : $"Kayıt: {m.Entry.FullName} ({m.Entry.Province})"); continue; }

            switch (m.Kind)
            {
                case MatchKind.Exact:
                    SetIl(row, m.Province); SetStatus(row, "Kayıtta var", null); break;
                case MatchKind.Probable:
                    SetIl(row, m.Province); SetStatus(row, "Benzer — kontrol edin", m.Note); break;
                case MatchKind.Ambiguous:
                    SetStatus(row, "Belirsiz", m.Note); break;
                default:
                    if (Cfg.PlayerProvinces.TryGetValue(key, out var learned) && learned.Length > 0)
                    { SetIl(row, learned); SetStatus(row, "Önceki turnuvadan", "Bu turnuvanın kayıt listesinde yok"); }
                    else SetStatus(row, reg is not { Entries.Count: > 0 } ? "Liste alınmadı" : "Kayıtta yok", null);
                    break;
            }
        }
        _loading = false;
        ApplyFilter();
        UpdateSummary();
    }

    private static void SetIl(DataGridViewRow row, string? il)
    {
        var col = (DataGridViewComboBoxColumn)row.DataGridView!.Columns["Il"]!;
        il ??= "";
        if (!col.Items.Contains(il)) col.Items.Add(il); // listede olmayan (yabancı/eski ad) değer
        row.Cells["Il"].Value = il;
    }

    private static void SetStatus(DataGridViewRow row, string status, string? note)
    {
        row.Cells["Durum"].Value = status;
        if (note is not null || status == "Elle") row.Cells["Not"].Value = note ?? row.Cells["Not"].Value;
        row.DefaultCellStyle.BackColor = status switch
        {
            "Benzer — kontrol edin" or "Önceki turnuvadan" => Yellow,
            "Belirsiz" => Orange,
            "Kayıtta yok" or "Liste alınmadı" => Grey,
            _ => Color.White
        };
    }

    private static bool NeedsCheck(DataGridViewRow r) => (r.Cells["Durum"].Value as string) is not ("Kayıtta var" or "Elle");

    private void ApplyFilter()
    {
        foreach (DataGridViewRow r in _grid.Rows)
            r.Visible = !_chkOnlyIssues.Checked || NeedsCheck(r);
    }

    private void UpdateSummary()
    {
        var rows = _grid.Rows.Cast<DataGridViewRow>().ToList();
        int Count(string s) => rows.Count(r => (r.Cells["Durum"].Value as string) == s);
        int withIl = rows.Count(r => !string.IsNullOrEmpty(r.Cells["Il"].Value as string));
        _lblSummary.Text = $"{rows.Count} sporcu · ili belli {withIl} · benzer {Count("Benzer — kontrol edin")} · belirsiz {Count("Belirsiz")} · " +
                           $"bulunamayan {Count("Kayıtta yok") + Count("Liste alınmadı")} · önceki turnuvadan {Count("Önceki turnuvadan")}";
    }

    /// <summary>
    /// Tablodaki illeri saklar: eşleşenler bu turnuvaya (Resolved), elle değişenler Overrides'a,
    /// ili belli herkes sonraki turnuvalar için öğrenilen il hafızasına.
    /// </summary>
    private void Save()
    {
        if (!Cfg.Registrations.TryGetValue(Key, out var reg))
        {
            if (_manual.Count == 0) return;
            reg = Cfg.Registrations[Key] = new EventRegistration { FetchedAt = DateTime.Now };
        }
        reg.Resolved.Clear();
        foreach (DataGridViewRow r in _grid.Rows)
        {
            var key = (string)r.Tag!;
            var il = r.Cells["Il"].Value as string ?? "";
            var status = r.Cells["Durum"].Value as string;
            if (_manual.TryGetValue(key, out var m)) reg.Overrides[key] = m;
            else if (status is "Kayıtta var" or "Benzer — kontrol edin" && il.Length > 0) reg.Resolved[key] = il;
            // Öğrenilen hafıza: yalnız kayıttan ya da elle gelen il (belirsiz/eski tahmin yazılmaz).
            if (il.Length > 0 && status is "Kayıtta var" or "Benzer — kontrol edin" or "Elle") Cfg.PlayerProvinces[key] = il;
        }
        _ctx.SaveConfig();
        _manual.Clear();
        _dirty = false;
    }
}
