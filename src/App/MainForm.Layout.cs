namespace NotasyonOtomasyonu.App;

/// <summary>
/// Sol sütunun içeriğe göre yerleşimi: kategori / tur / hızlı erişim düğme alanları kaydırma
/// çubuğu yerine içerik kadar uzar (en çok <see cref="MaxRowsWithoutScroll"/> satır); alttaki
/// bilgi kutusu, kısayollar, durum ve günlük kendiliğinden aşağı kayar. Çok uzun listelerde
/// (3 satırı aşınca) alan sabitlenir ve yalnız o zaman kaydırma çıkar.
/// Ölçüler tasarım yerleşiminden (DPI ölçeklemesi uygulandıktan sonra) okunur.
/// </summary>
public partial class MainForm
{
    private const int MaxRowsWithoutScroll = 3;

    private bool _layoutReady;
    private int _gapCatToRound, _roundLabelDy, _gapRoundToLink, _onlinePadBottom;
    private int _gapGroupToInfo, _excludeDy, _gapInfoToQuick, _quickPadBottom, _gapToStatus, _gapStatusToLog;
    private int _minLogHeight;

    /// <summary>Tasarımdaki boşlukları kaydeder (Load'da, ölçekleme sonrası bir kez).</summary>
    private void CaptureLayout()
    {
        _gapCatToRound = flowRounds.Top - flowCategories.Bottom;
        _roundLabelDy = lblRound.Top - flowRounds.Top;
        _gapRoundToLink = lnkAdvanced.Top - flowRounds.Bottom;
        _onlinePadBottom = grpOnline.Height - lnkAdvanced.Bottom;
        _gapGroupToInfo = lblInfo.Top - grpOnline.Bottom;
        _excludeDy = btnExclude.Top - lblInfo.Top;
        _gapInfoToQuick = grpQuick.Top - lblInfo.Bottom;
        _quickPadBottom = grpQuick.Height - Math.Max(flowQuick.Bottom, btnPrintAll.Bottom);
        _gapToStatus = lblStatus.Top - grpQuick.Bottom;
        _gapStatusToLog = txtLog.Top - lblStatus.Bottom;
        _minLogHeight = Math.Max(60, flowRounds.Height * 2);
        _layoutReady = true;
    }

    /// <summary>Düğme alanlarını içeriğe göre boyutlandırıp sol sütunu yeniden dizer.</summary>
    private void RelayoutLeft()
    {
        if (!_layoutReady) return;
        SuspendLayout();
        try
        {
            // --- chess-results grubu: kategori → tur → gelişmiş bağlantı ---
            FitFlow(flowCategories);
            flowRounds.Top = flowCategories.Bottom + _gapCatToRound;
            FitFlow(flowRounds);
            lblRound.Top = flowRounds.Top + _roundLabelDy;
            btnSync.Top = flowRounds.Top;
            lnkAdvanced.Top = flowRounds.Bottom + _gapRoundToLink;
            grpOnline.Height = lnkAdvanced.Bottom + _onlinePadBottom;

            // --- bilgi kutusu: görünen kaynak grubunun altında ---
            var source = rbOnline.Checked ? (Control)grpOnline : grpFile;
            lblInfo.Top = source.Bottom + _gapGroupToInfo;
            btnEditInfo.Top = lblInfo.Top;
            btnExclude.Top = lblInfo.Top + _excludeDy;

            // --- hızlı erişim ---
            Control above = lblInfo;
            if (grpQuick.Visible)
            {
                grpQuick.Top = lblInfo.Bottom + _gapInfoToQuick;
                FitFlow(flowQuick, maxRows: int.MaxValue); // hızlı erişim kaydırmasız: düğmeler alt satırlara akar
                grpQuick.Height = Math.Max(flowQuick.Bottom, btnPrintAll.Bottom) + _quickPadBottom;
                above = grpQuick;
            }

            // --- pano listeleri (eşleştirme · sonuç): hızlı erişimin altında, küçük ---
            if (_grpLists is { Visible: true })
            {
                _grpLists.SetBounds(grpQuick.Left, above.Bottom + _gapInfoToQuick, grpQuick.Width, _grpLists.Height);
                LayoutListGroup();
                above = _grpLists;
            }

            // --- durum + günlük ---
            lblStatus.Top = above.Bottom + _gapToStatus;
            // İşlem kaydı: başlık bağlantısı hep görünür; kutu yalnız açıkken (kapalıyken sol sütun kısalır).
            int logTop = lblStatus.Bottom + _gapStatusToLog;
            if (_lnkLog is not null)
            {
                _lnkLog.Top = logTop;
                PlaceLogWindowLink();
                logTop = _lnkLog.Bottom + 2;
            }
            txtLog.Top = logTop;

            // Sol sütun pencereye sığmıyorsa pencereyi (ekranın izin verdiği kadar) uzat.
            int needed = txtLog.Visible ? txtLog.Top + _minLogHeight + 12 : logTop + 12;
            if (needed > ClientSize.Height && WindowState == FormWindowState.Normal)
            {
                var screen = Screen.FromControl(this).WorkingArea;
                int extra = Math.Min(needed - ClientSize.Height, Math.Max(0, screen.Bottom - Bottom));
                if (extra > 0) Height += extra;
            }
            FitLog();
        }
        finally { ResumeLayout(true); }
    }

    /// <summary>
    /// Akış panelini içeriğinin gerçek yüksekliğine getirir; <see cref="MaxRowsWithoutScroll"/>
    /// satırı aşarsa o yükseklikte sabitler ve yalnız o zaman kaydırma açar.
    /// </summary>
    private static void FitFlow(FlowLayoutPanel panel, int maxRows = MaxRowsWithoutScroll)
    {
        var items = panel.Controls.Cast<Control>().ToList();
        int rowH = items.Count > 0
            ? items.Max(c => c.GetPreferredSize(Size.Empty).Height + c.Margin.Vertical)
            : panel.Height;
        if (items.Count == 0)
        {
            panel.AutoScroll = false;
            return; // boşken tasarım yüksekliği kalsın (tek satır)
        }

        // Kaydırma çubuğu olmadan gereken yükseklik (panel genişliğine göre satırlara bölünmüş).
        panel.AutoScroll = false;
        int needed = panel.GetPreferredSize(new Size(panel.ClientSize.Width, 0)).Height;
        int maxH = maxRows == int.MaxValue ? int.MaxValue : rowH * maxRows + panel.Padding.Vertical;
        if (needed <= maxH)
        {
            panel.Height = Math.Max(rowH + panel.Padding.Vertical, needed);
        }
        else
        {
            panel.Height = maxH;
            panel.AutoScroll = true; // çok uzun liste: yalnız bu durumda kaydırma
        }
    }
}
