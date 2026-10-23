namespace NotasyonOtomasyonu.App;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private Panel pnlHeader;
    private PictureBox pbLogo;
    private Label lblTitle;
    private Button btnSettings;

    private Label lblSource;
    private RadioButton rbOnline;
    private RadioButton rbFile;

    private GroupBox grpOnline;
    private Label lblSearch;
    private TextBox txtSearch;
    private Button btnSearch;
    private Label lblProvince;
    private ComboBox cboProvince;
    private Label lblTournament;
    private ComboBox cboTournament;
    private LinkLabel lnkAdvanced;
    private Label lblCategory;
    private FlowLayoutPanel flowCategories;
    private Label lblRound;
    private FlowLayoutPanel flowRounds;
    private Button btnSync;

    private GroupBox grpFile;
    private TextBox txtFile;
    private Button btnBrowse;
    private Label lblFileRound;
    private NumericUpDown numRound;

    private Label lblInfo;
    private Button btnEditInfo;
    private Button btnExclude;
    private GroupBox grpQuick;
    private FlowLayoutPanel flowQuick;
    private Button btnPrintAll;
    private Label lblStatus;
    private TextBox txtLog;

    // Sağ panel: basılacak masalar + yazdırma
    private GroupBox grpPairings;
    private DataGridView dgvPairings;
    private Button btnSelectAll;
    private Button btnSelectNone;
    private Label lblSelection;
    private Label lblCopies;
    private NumericUpDown numCopies;
    private Button btnPdf;
    private Button btnPrint;

    private void InitializeComponent()
    {
        var cream = System.Drawing.Color.FromArgb(245, 245, 240);
        var green = System.Drawing.Color.FromArgb(118, 150, 86);
        var greenDark = System.Drawing.Color.FromArgb(95, 122, 70);
        var leftAnchor = AnchorStyles.Top | AnchorStyles.Left;

        pnlHeader = new Panel();
        pbLogo = new PictureBox();
        lblTitle = new Label();
        btnSettings = new Button();
        lblSource = new Label();
        rbOnline = new RadioButton();
        rbFile = new RadioButton();
        grpOnline = new GroupBox();
        lblSearch = new Label();
        txtSearch = new TextBox();
        btnSearch = new Button();
        lblProvince = new Label();
        cboProvince = new ComboBox();
        lblTournament = new Label();
        cboTournament = new ComboBox();
        lnkAdvanced = new LinkLabel();
        lblCategory = new Label();
        flowCategories = new FlowLayoutPanel();
        lblRound = new Label();
        flowRounds = new FlowLayoutPanel();
        btnSync = new Button();
        grpFile = new GroupBox();
        txtFile = new TextBox();
        btnBrowse = new Button();
        lblFileRound = new Label();
        numRound = new NumericUpDown();
        lblInfo = new Label();
        btnEditInfo = new Button();
        btnExclude = new Button();
        grpQuick = new GroupBox();
        flowQuick = new FlowLayoutPanel();
        btnPrintAll = new Button();
        lblStatus = new Label();
        txtLog = new TextBox();
        grpPairings = new GroupBox();
        dgvPairings = new DataGridView();
        btnSelectAll = new Button();
        btnSelectNone = new Button();
        lblSelection = new Label();
        lblCopies = new Label();
        numCopies = new NumericUpDown();
        btnPdf = new Button();
        btnPrint = new Button();
        ((System.ComponentModel.ISupportInitialize)numRound).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numCopies).BeginInit();
        ((System.ComponentModel.ISupportInitialize)dgvPairings).BeginInit();
        grpOnline.SuspendLayout();
        grpFile.SuspendLayout();
        grpQuick.SuspendLayout();
        grpPairings.SuspendLayout();
        SuspendLayout();

        // Header
        pnlHeader.BackColor = green;
        pnlHeader.Dock = DockStyle.Top;
        pnlHeader.Height = 56;
        pnlHeader.Controls.Add(btnSettings);
        pnlHeader.Controls.Add(lblTitle);
        pnlHeader.Controls.Add(pbLogo);
        ((System.ComponentModel.ISupportInitialize)pbLogo).BeginInit();
        pbLogo.Location = new System.Drawing.Point(12, 8);
        pbLogo.Size = new System.Drawing.Size(40, 40);
        pbLogo.SizeMode = PictureBoxSizeMode.Zoom;
        pbLogo.BackColor = System.Drawing.Color.Transparent;
        lblTitle.Location = new System.Drawing.Point(58, 0);
        lblTitle.Size = new System.Drawing.Size(390, 56);
        lblTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        lblTitle.ForeColor = System.Drawing.Color.White;
        lblTitle.Font = new System.Drawing.Font("Segoe UI", 15F, System.Drawing.FontStyle.Bold);
        lblTitle.Text = "Notasyon Kâğıdı Yazdırıcı";
        lblTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        ((System.ComponentModel.ISupportInitialize)pbLogo).EndInit();
        btnSettings.Text = "⚙ Ayarlar";
        btnSettings.Size = new System.Drawing.Size(104, 34);
        btnSettings.Location = new System.Drawing.Point(448, 11);
        btnSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSettings.FlatStyle = FlatStyle.Flat;
        btnSettings.BackColor = System.Drawing.Color.White;
        btnSettings.ForeColor = greenDark;
        btnSettings.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
        btnSettings.FlatAppearance.BorderColor = greenDark;
        btnSettings.Click += btnSettings_Click;

        // ================= SOL SÜTUN: turnuva / kategori / tur =================
        lblSource.AutoSize = true;
        lblSource.Location = new System.Drawing.Point(18, 70);
        lblSource.Text = "Veri kaynağı:";
        rbOnline.AutoSize = true;
        rbOnline.Location = new System.Drawing.Point(110, 68);
        rbOnline.Text = "chess-results (online)";
        rbOnline.Checked = true;
        rbOnline.CheckedChanged += source_CheckedChanged;
        rbFile.AutoSize = true;
        rbFile.Location = new System.Drawing.Point(290, 68);
        rbFile.Text = "Dosya (JSON/CSV/XLSX)";
        rbFile.CheckedChanged += source_CheckedChanged;

        // grpOnline
        grpOnline.Text = "chess-results'tan çek";
        grpOnline.Location = new System.Drawing.Point(18, 96);
        grpOnline.Size = new System.Drawing.Size(528, 192);
        grpOnline.Anchor = leftAnchor;
        grpOnline.Controls.Add(lblSearch);
        grpOnline.Controls.Add(txtSearch);
        grpOnline.Controls.Add(btnSearch);
        grpOnline.Controls.Add(lblProvince);
        grpOnline.Controls.Add(cboProvince);
        grpOnline.Controls.Add(lblTournament);
        grpOnline.Controls.Add(cboTournament);
        grpOnline.Controls.Add(lblCategory);
        grpOnline.Controls.Add(flowCategories);
        grpOnline.Controls.Add(lblRound);
        grpOnline.Controls.Add(flowRounds);
        grpOnline.Controls.Add(btnSync);
        grpOnline.Controls.Add(lnkAdvanced);

        // 1. ADIM — Turnuvayı bul: metin yaz + il seç, SONRA en sağdaki "Ara" butonuna bas.
        lblSearch.AutoSize = true; lblSearch.Location = new System.Drawing.Point(12, 24); lblSearch.Text = "1) Ara:";
        lblSearch.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
        txtSearch.Location = new System.Drawing.Point(64, 21); txtSearch.Size = new System.Drawing.Size(148, 25);
        lblProvince.AutoSize = true; lblProvince.Location = new System.Drawing.Point(218, 24); lblProvince.Text = "İl:";
        cboProvince.Location = new System.Drawing.Point(240, 21); cboProvince.Size = new System.Drawing.Size(150, 25);
        cboProvince.DropDownStyle = ComboBoxStyle.DropDownList;
        cboProvince.SelectedIndexChanged += cboProvince_SelectedIndexChanged;
        btnSearch.Location = new System.Drawing.Point(396, 20); btnSearch.Size = new System.Drawing.Size(120, 27);
        btnSearch.Text = "🔍 Ara"; btnSearch.Click += btnSearch_Click;
        btnSearch.BackColor = green; btnSearch.ForeColor = System.Drawing.Color.White; btnSearch.FlatStyle = FlatStyle.Flat;

        lblTournament.AutoSize = true; lblTournament.Location = new System.Drawing.Point(12, 54); lblTournament.Text = "Turnuva:";
        cboTournament.Location = new System.Drawing.Point(76, 51); cboTournament.Size = new System.Drawing.Size(440, 25);
        cboTournament.DropDownStyle = ComboBoxStyle.DropDownList;
        cboTournament.SelectedIndexChanged += cboTournament_SelectedIndexChanged;

        // 2. ADIM — Kategori ve tur seç (seçince eşleştirmeler kendiliğinden çekilir)
        lblCategory.AutoSize = true; lblCategory.Location = new System.Drawing.Point(12, 86); lblCategory.Text = "2) Kategori:";
        lblCategory.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
        flowCategories.Location = new System.Drawing.Point(96, 80);
        flowCategories.Size = new System.Drawing.Size(420, 38);
        flowCategories.AutoScroll = true;
        flowCategories.WrapContents = true;

        lblRound.AutoSize = true; lblRound.Location = new System.Drawing.Point(12, 126); lblRound.Text = "Tur:";
        flowRounds.Location = new System.Drawing.Point(96, 120);
        flowRounds.Size = new System.Drawing.Size(356, 38);
        flowRounds.AutoScroll = true;
        flowRounds.WrapContents = true;

        // Eşleştirmeleri yeniden çek (F5) — chess-results'ta değişiklik olduysa.
        btnSync.Location = new System.Drawing.Point(460, 120); btnSync.Size = new System.Drawing.Size(56, 38);
        btnSync.Text = "🔄";
        btnSync.Font = new System.Drawing.Font("Segoe UI", 12F);
        btnSync.BackColor = green; btnSync.ForeColor = System.Drawing.Color.White; btnSync.FlatStyle = FlatStyle.Flat;
        btnSync.FlatAppearance.BorderColor = greenDark;
        btnSync.Click += btnSync_Click;

        lnkAdvanced.AutoSize = true; lnkAdvanced.Location = new System.Drawing.Point(12, 166);
        lnkAdvanced.Text = "▸ Gelişmiş: chess-results link/no ile getir";
        lnkAdvanced.LinkColor = greenDark;
        lnkAdvanced.LinkClicked += lnkAdvanced_LinkClicked;

        // grpFile
        grpFile.Text = "Dosyadan oku";
        grpFile.Location = new System.Drawing.Point(18, 96);
        grpFile.Size = new System.Drawing.Size(528, 100);
        grpFile.Anchor = leftAnchor;
        grpFile.Visible = false;
        grpFile.Controls.Add(txtFile);
        grpFile.Controls.Add(btnBrowse);
        grpFile.Controls.Add(lblFileRound);
        grpFile.Controls.Add(numRound);
        txtFile.Location = new System.Drawing.Point(14, 28); txtFile.Size = new System.Drawing.Size(400, 25);
        txtFile.ReadOnly = true; txtFile.BackColor = System.Drawing.Color.White;
        btnBrowse.Location = new System.Drawing.Point(420, 27); btnBrowse.Size = new System.Drawing.Size(96, 27);
        btnBrowse.Text = "Gözat…"; btnBrowse.Click += btnBrowse_Click;
        lblFileRound.AutoSize = true; lblFileRound.Location = new System.Drawing.Point(14, 64); lblFileRound.Text = "Tur No:";
        numRound.Location = new System.Drawing.Point(80, 62); numRound.Size = new System.Drawing.Size(70, 25);
        numRound.Minimum = 1; numRound.Maximum = 50; numRound.Value = 1;

        // Turnuva bilgi özeti (otomatik dolu) + Düzenle
        lblInfo.Location = new System.Drawing.Point(20, 296);
        lblInfo.Size = new System.Drawing.Size(420, 56);
        lblInfo.BorderStyle = BorderStyle.FixedSingle;
        lblInfo.BackColor = System.Drawing.Color.White;
        lblInfo.Padding = new Padding(8, 6, 6, 6);
        lblInfo.TextAlign = System.Drawing.ContentAlignment.TopLeft;
        lblInfo.Anchor = leftAnchor;
        lblInfo.Text = "Turnuva: —";
        btnEditInfo.Location = new System.Drawing.Point(448, 296);
        btnEditInfo.Size = new System.Drawing.Size(98, 27);
        btnEditInfo.Text = "✏ Düzenle";
        btnEditInfo.Anchor = leftAnchor;
        btnEditInfo.Click += btnEditInfo_Click;
        btnExclude.Location = new System.Drawing.Point(448, 325);
        btnExclude.Size = new System.Drawing.Size(98, 27);
        btnExclude.Text = "🚫 Hariç Tut";
        btnExclude.Anchor = leftAnchor;
        btnExclude.Click += btnExclude_Click;

        // Hızlı yazdır (kısayollar) — kategori başına tek tık + tüm kategoriler.
        grpQuick.Text = "⚡ Hızlı erişim — kategori · son tur";
        grpQuick.Location = new System.Drawing.Point(18, 358);
        grpQuick.Size = new System.Drawing.Size(528, 70);
        grpQuick.Anchor = leftAnchor;
        grpQuick.Controls.Add(flowQuick);
        grpQuick.Controls.Add(btnPrintAll);
        flowQuick.Location = new System.Drawing.Point(10, 20);
        flowQuick.Size = new System.Drawing.Size(366, 42);
        flowQuick.AutoScroll = true;
        flowQuick.WrapContents = true;
        btnPrintAll.Location = new System.Drawing.Point(384, 20);
        btnPrintAll.Size = new System.Drawing.Size(132, 40);
        btnPrintAll.Text = "★ Tüm\nKategoriler";
        btnPrintAll.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
        btnPrintAll.BackColor = green; btnPrintAll.ForeColor = System.Drawing.Color.White; btnPrintAll.FlatStyle = FlatStyle.Flat;
        btnPrintAll.FlatAppearance.BorderColor = greenDark;
        btnPrintAll.Enabled = false;
        btnPrintAll.Click += btnPrintAll_Click;

        // Durum / log (sol alt)
        lblStatus.Location = new System.Drawing.Point(20, 438); lblStatus.Size = new System.Drawing.Size(526, 20);
        lblStatus.AutoEllipsis = true;
        lblStatus.ForeColor = greenDark; lblStatus.Text = "Hazır.";
        txtLog.Location = new System.Drawing.Point(20, 460); txtLog.Size = new System.Drawing.Size(526, 164);
        txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical; txtLog.BackColor = System.Drawing.Color.White;
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Left; // yükseklik FitLog() ile pencereye uydurulur

        // ================= SAĞ SÜTUN: 3) basılacak masalar + yazdır =================
        grpPairings.Text = "3) Basılacak masalar — işareti kaldırılan masa basılmaz";
        grpPairings.Location = new System.Drawing.Point(562, 64);
        grpPairings.Size = new System.Drawing.Size(500, 560);
        grpPairings.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grpPairings.Controls.Add(dgvPairings);
        grpPairings.Controls.Add(btnSelectAll);
        grpPairings.Controls.Add(btnSelectNone);
        grpPairings.Controls.Add(lblSelection);
        grpPairings.Controls.Add(lblCopies);
        grpPairings.Controls.Add(numCopies);
        grpPairings.Controls.Add(btnPdf);
        grpPairings.Controls.Add(btnPrint);

        dgvPairings.Location = new System.Drawing.Point(10, 24);
        dgvPairings.Size = new System.Drawing.Size(480, 400);
        dgvPairings.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        dgvPairings.AllowUserToAddRows = false;
        dgvPairings.AllowUserToDeleteRows = false;
        dgvPairings.AllowUserToResizeRows = false;
        dgvPairings.RowHeadersVisible = false;
        dgvPairings.ShowCellToolTips = false;
        dgvPairings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgvPairings.MultiSelect = true;
        dgvPairings.BackgroundColor = System.Drawing.Color.White;
        dgvPairings.BorderStyle = BorderStyle.FixedSingle;
        dgvPairings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgvPairings.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        dgvPairings.EnableHeadersVisualStyles = false;
        dgvPairings.ColumnHeadersDefaultCellStyle.BackColor = cream;
        dgvPairings.ColumnHeadersDefaultCellStyle.ForeColor = greenDark;
        dgvPairings.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
        dgvPairings.ColumnHeadersDefaultCellStyle.SelectionBackColor = cream;
        dgvPairings.ColumnHeadersDefaultCellStyle.SelectionForeColor = greenDark;
        dgvPairings.DefaultCellStyle.ForeColor = System.Drawing.Color.Black;
        dgvPairings.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(224, 231, 216);
        dgvPairings.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.Black;
        dgvPairings.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colPrint", HeaderText = "Bas", FillWeight = 9 });
        dgvPairings.Columns.Add(new DataGridViewTextBoxColumn { Name = "colBoard", HeaderText = "Masa", FillWeight = 11, ReadOnly = true });
        dgvPairings.Columns.Add(new DataGridViewTextBoxColumn { Name = "colWhite", HeaderText = "Beyaz", FillWeight = 30, ReadOnly = true });
        dgvPairings.Columns.Add(new DataGridViewTextBoxColumn { Name = "colWRtg", HeaderText = "ELO", FillWeight = 10, ReadOnly = true });
        dgvPairings.Columns.Add(new DataGridViewTextBoxColumn { Name = "colWIl", HeaderText = "İl", FillWeight = 12, ReadOnly = true });
        dgvPairings.Columns.Add(new DataGridViewTextBoxColumn { Name = "colBlack", HeaderText = "Siyah", FillWeight = 30, ReadOnly = true });
        dgvPairings.Columns.Add(new DataGridViewTextBoxColumn { Name = "colBRtg", HeaderText = "ELO", FillWeight = 10, ReadOnly = true });
        dgvPairings.Columns.Add(new DataGridViewTextBoxColumn { Name = "colBIl", HeaderText = "İl", FillWeight = 12, ReadOnly = true });

        btnSelectAll.Location = new System.Drawing.Point(10, 430); btnSelectAll.Size = new System.Drawing.Size(90, 26);
        btnSelectAll.Text = "☑ Tümü"; btnSelectAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnSelectAll.Click += btnSelectAll_Click;
        btnSelectNone.Location = new System.Drawing.Point(104, 430); btnSelectNone.Size = new System.Drawing.Size(90, 26);
        btnSelectNone.Text = "☐ Hiçbiri"; btnSelectNone.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnSelectNone.Click += btnSelectNone_Click;
        lblSelection.Location = new System.Drawing.Point(202, 434); lblSelection.Size = new System.Drawing.Size(288, 20);
        lblSelection.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lblSelection.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
        lblSelection.ForeColor = greenDark;
        lblSelection.Text = "Eşleştirme yok.";

        lblCopies.AutoSize = true; lblCopies.Location = new System.Drawing.Point(10, 476); lblCopies.Text = "Nüsha:";
        lblCopies.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        numCopies.Location = new System.Drawing.Point(62, 473); numCopies.Size = new System.Drawing.Size(52, 25);
        numCopies.Minimum = 1; numCopies.Maximum = 10; numCopies.Value = 2;
        numCopies.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        numCopies.ValueChanged += numCopies_ValueChanged;

        btnPdf.Location = new System.Drawing.Point(10, 506); btnPdf.Size = new System.Drawing.Size(104, 44);
        btnPdf.Text = "📄 PDF";
        btnPdf.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnPdf.Click += btnPdf_Click;

        btnPrint.Location = new System.Drawing.Point(124, 470); btnPrint.Size = new System.Drawing.Size(366, 80);
        btnPrint.Text = "🖨  Yazdır";
        btnPrint.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
        btnPrint.BackColor = green; btnPrint.ForeColor = System.Drawing.Color.White; btnPrint.FlatStyle = FlatStyle.Flat;
        btnPrint.FlatAppearance.BorderColor = greenDark;
        btnPrint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        btnPrint.Click += btnPrint_Click;

        // MainForm
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = cream;
        ClientSize = new System.Drawing.Size(1074, 636);
        MinimumSize = new System.Drawing.Size(1000, 640);
        KeyPreview = true;
        Controls.Add(grpPairings);
        Controls.Add(txtLog);
        Controls.Add(lblStatus);
        Controls.Add(grpQuick);
        Controls.Add(btnExclude);
        Controls.Add(btnEditInfo);
        Controls.Add(lblInfo);
        Controls.Add(grpFile);
        Controls.Add(grpOnline);
        Controls.Add(rbFile);
        Controls.Add(rbOnline);
        Controls.Add(lblSource);
        Controls.Add(pnlHeader);
        Font = new System.Drawing.Font("Segoe UI", 9.75F);
        Text = "Chess Scoresheet Printer";
        StartPosition = FormStartPosition.CenterScreen;
        FormClosing += MainForm_FormClosing;
        KeyDown += MainForm_KeyDown;

        ((System.ComponentModel.ISupportInitialize)numRound).EndInit();
        ((System.ComponentModel.ISupportInitialize)numCopies).EndInit();
        ((System.ComponentModel.ISupportInitialize)dgvPairings).EndInit();
        grpOnline.ResumeLayout(false);
        grpOnline.PerformLayout();
        grpFile.ResumeLayout(false);
        grpFile.PerformLayout();
        grpQuick.ResumeLayout(false);
        grpPairings.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
