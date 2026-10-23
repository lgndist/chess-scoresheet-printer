using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Eşleştirme yokken (turnuva henüz chess-results'ta değil) "Basılacak masalar" alanında TSF kayıt
/// listesinin önizlemesi: kayıtlı sporcular kategoriye göre gruplu (grup başlığında sporcu ve masa
/// sayısı) ya da kayıt sırasıyla; ad soyad, ELO, UKD, il ve kategori sütunlarıyla. Kategori
/// düğmesine basılınca o kategorinin grubuna gidilir.
/// </summary>
public partial class MainForm
{
    private DataGridView? _dgvRegs;
    private ComboBox? _cboRegGroup;
    private bool _regView;

    private void InitRegistrationView()
    {
        _dgvRegs = new DataGridView
        {
            Visible = false, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = dgvPairings.BackgroundColor, BorderStyle = dgvPairings.BorderStyle, GridColor = dgvPairings.GridColor,
            EnableHeadersVisualStyles = dgvPairings.EnableHeadersVisualStyles,
            ColumnHeadersDefaultCellStyle = dgvPairings.ColumnHeadersDefaultCellStyle.Clone(),
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = dgvPairings.ColumnHeadersHeight,
            DefaultCellStyle = dgvPairings.DefaultCellStyle.Clone(),
            Font = dgvPairings.Font
        };
        _dgvRegs.RowTemplate.Height = dgvPairings.RowTemplate.Height;
        // Grup başlığı satırı tüm satır boyunca tek yazı olarak çizilir (hücreye sığmayıp kesilmesin).
        _dgvRegs.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || _dgvRegs.Rows[e.RowIndex].Tag is not string title) return;
            e.Handled = true;
            if (e.ColumnIndex != _dgvRegs.Columns.GetFirstColumn(DataGridViewElementStates.Visible)!.Index) return;
            var row = _dgvRegs.GetRowDisplayRectangle(e.RowIndex, cutOverflow: true);
            var st = _dgvRegs.Rows[e.RowIndex].DefaultCellStyle;
            bool sel = _dgvRegs.Rows[e.RowIndex].Selected;
            using var bg = new SolidBrush(sel ? st.SelectionBackColor : st.BackColor);
            e.Graphics!.FillRectangle(bg, row);
            TextRenderer.DrawText(e.Graphics, title, st.Font ?? _dgvRegs.Font, new Rectangle(row.X + 6, row.Y, row.Width - 12, row.Height),
                                  sel ? st.SelectionForeColor : st.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            using var pen = new Pen(_dgvRegs.GridColor);
            e.Graphics.DrawLine(pen, row.Left, row.Bottom - 1, row.Right, row.Bottom - 1);
        };
        void Col(string name, string header, float weight, bool center = false)
        {
            var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (center) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _dgvRegs.Columns.Add(c);
        }
        Col("No", "No", 7, center: true);
        Col("Name", "Ad Soyad", 36);
        Col("Elo", "ELO", 9, center: true);
        Col("Ukd", "UKD", 9, center: true);
        Col("Il", "İl", 22);
        Col("Cat", "Kategori", 22);
        grpPairings.Controls.Add(_dgvRegs);

        _cboRegGroup = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Visible = false, Font = btnSelectAll.Font };
        _cboRegGroup.Items.AddRange(new object[] { "Kategoriye göre grupla", "Kayıt sırasıyla" });
        _cboRegGroup.SelectedIndex = 0;
        _cboRegGroup.SelectedIndexChanged += (_, _) => { if (_regView) ShowRegistrations(); };
        grpPairings.Controls.Add(_cboRegGroup);

        // Önizleme ızgarası eşleştirme ızgarasının yerini birebir alır.
        void Sync()
        {
            _dgvRegs.Bounds = dgvPairings.Bounds; _dgvRegs.Anchor = dgvPairings.Anchor;
            _cboRegGroup.SetBounds(btnSelectAll.Left, btnSelectAll.Top + 1, btnSelectNone.Right - btnSelectAll.Left, btnSelectAll.Height);
            _cboRegGroup.Anchor = btnSelectAll.Anchor;
        }
        dgvPairings.SizeChanged += (_, _) => Sync();
        dgvPairings.LocationChanged += (_, _) => Sync();
        btnSelectAll.LocationChanged += (_, _) => Sync();
        Sync();
    }

    /// <summary>Eşleştirme ızgarası ile kayıt önizlemesi arasında geçiş.</summary>
    private void SetRegistrationView(bool on)
    {
        if (_dgvRegs is null || _cboRegGroup is null) return;
        _regView = on;
        _dgvRegs.Visible = _cboRegGroup.Visible = on;
        dgvPairings.Visible = btnSelectAll.Visible = btnSelectNone.Visible = !on;
        if (_btnGridList is not null) _btnGridList.Visible = !on;
        if (on) { _dgvRegs.BringToFront(); _cboRegGroup.BringToFront(); }
    }

    /// <summary>Açık kayıttaki turnuvanın kayıt listesini önizlemede gösterir.</summary>
    private void ShowRegistrations()
    {
        if (_dgvRegs is null || _cboRegGroup is null || _eventTnr is not int tnr) return;
        var reg = _config.Registrations.GetValueOrDefault(tnr.ToString());
        _dgvRegs.SuspendLayout();
        _dgvRegs.Rows.Clear();
        if (reg is null || reg.Entries.Count == 0)
        {
            grpPairings.Text = _busy ? "3) Kayıtlı sporcular — TSF kayıt listesi okunuyor…" : "3) Kayıtlı sporcular — TSF kayıt listesi alınamadı";
            lblSelection.Text = "";
            _dgvRegs.ResumeLayout();
            return;
        }

        var counts = RegistrationCategories.Counts(reg);
        var headerStyle = new DataGridViewCellStyle(_dgvRegs.DefaultCellStyle)
        {
            BackColor = Color.FromArgb(232, 240, 224), SelectionBackColor = Color.FromArgb(214, 228, 200),
            ForeColor = ThGreenDark, SelectionForeColor = ThGreenDark, Font = new Font(_dgvRegs.Font, FontStyle.Bold)
        };
        void Player(int no, PlayerRegistration e, string cat)
            => _dgvRegs.Rows.Add(no, e.FullName, e.Elo?.ToString() ?? "", e.Ukd?.ToString() ?? "", Provinces.Print(e.Province), cat);

        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _dgvRegs.Columns["Cat"]!.Visible = _cboRegGroup.SelectedIndex != 0; // gruplu görünümde kategori başlıkta yazar
        if (_cboRegGroup.SelectedIndex == 0)
        {
            foreach (var c in counts)
            {
                int h = _dgvRegs.Rows.Add("", "", "", "", "", "");
                _dgvRegs.Rows[h].DefaultCellStyle = headerStyle;
                _dgvRegs.Rows[h].Tag = $"{c.Name} — {c.Count} sporcu · {c.Boards} masa{(c.Count % 2 == 1 ? " (1 BAY)" : "")}";
                headers[c.Name] = h;
                int no = 0;
                foreach (var e in reg.Entries.Where(e => RegistrationCategories.CategoryOf(e, reg) == c.Name))
                    Player(++no, e, c.Name);
            }
        }
        else
        {
            int no = 0;
            foreach (var e in reg.Entries) Player(++no, e, RegistrationCategories.CategoryOf(e, reg));
        }
        _dgvRegs.ResumeLayout();
        _dgvRegs.ClearSelection();

        // Seçili kategori düğmesinin grubuna git
        if (SelectedCategory() is { } sel && headers.TryGetValue(sel.Name, out var row))
        {
            _dgvRegs.FirstDisplayedScrollingRowIndex = row;
            _dgvRegs.Rows[row].Selected = true;
        }

        int boards = counts.Sum(c => c.Boards);
        grpPairings.Text = $"3) Kayıtlı sporcular — TSF kayıt listesi{(TsfClosed.Contains(-tnr) ? " (kayıtlar bitti)" : "")} • eşleştirme henüz yok";
        lblSelection.Text = $"{reg.Entries.Count} sporcu · {counts.Count} kategori · {boards} masa";
    }
}
