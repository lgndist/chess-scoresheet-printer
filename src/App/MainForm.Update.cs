using NotasyonOtomasyonu.App.Update;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Güncelleme bildirimi: açılışta (günde en çok bir kez, Ayarlar'dan kapatılabilir) GitHub'daki son sürüm
/// arka planda denetlenir; yeniyse başlıkta "⬆ Yeni sürüm" düğmesi çıkar. Güncellemeyle açılınca da
/// durum satırında bildirilir.
/// </summary>
public partial class MainForm
{
    private Button? _btnUpdate;

    /// <summary>
    /// Güncellemeyle açıldıysa bildirir. Açılıştaki turnuva yüklemesi bittikten sonra çağrılır: yoksa
    /// yüklemenin durum mesajları bildirimi hemen eziyordu.
    /// </summary>
    private void AnnounceUpdate()
    {
        if (Program.UpdatedFrom is not { } from) return;
        SetStatus($"⬆ Program v{Updater.Current} sürümüne güncellendi (önceki: v{from}). Ayarlarınız korundu.", ok: true);
        Log($"⬆ Güncellendi: v{from} → v{Updater.Current}");
    }

    private async Task CheckForUpdateOnStartAsync()
    {
        var u = _config.Updates;
        if (!u.AutoCheck || u.LastCheck is { } last && DateTime.Now - last < TimeSpan.FromHours(20)) return;
        try
        {
            var r = await Updater.LatestAsync();
            u.LastCheck = DateTime.Now;
            _config.Save(_configPath);
            if (r is not null && Updater.IsNewer(r) && u.SkippedVersion != r.Version.ToString()) ShowUpdateButton(r);
        }
        catch { /* çevrimdışı: sessizce geç, sonraki açılışta yine denenir */ }
    }

    private void ShowUpdateButton(ReleaseInfo r)
    {
        if (_btnUpdate is null)
        {
            _btnUpdate = HeaderButton("", "");
            _btnUpdate.BackColor = Color.FromArgb(255, 236, 160);
            _btnUpdate.ForeColor = Color.FromArgb(90, 60, 0);
            _btnUpdate.FlatAppearance.BorderColor = Color.FromArgb(200, 160, 40);
        }
        _btnUpdate.Text = $"⬆ Yeni sürüm v{r.Version}";
        new ToolTip().SetToolTip(_btnUpdate, "Yeni sürümün notlarını görün; tek tıkla indirilip kurulur, ayarlarınız korunur.");
        _btnUpdate.Click -= UpdateButtonClick;
        _btnUpdate.Click += UpdateButtonClick;
        _btnUpdate.Tag = r;
        PositionHeader();
    }

    private void UpdateButtonClick(object? sender, EventArgs e) => OpenUpdates((sender as Button)?.Tag as ReleaseInfo);

    /// <summary>Güncelleme penceresi (Ayarlar → Güncelleme ya da başlıktaki bildirim).</summary>
    internal void OpenUpdates(ReleaseInfo? release, IWin32Window? owner = null)
    {
        UpdateConfigFromUi();
        using var f = new UpdateForm(_config.Updates, () => _config.Save(_configPath), release) { Icon = Icon };
        f.ShowDialog(owner ?? this);
        // Bu sürüm atlandıysa bildirim kalksın
        if (_btnUpdate is { Tag: ReleaseInfo r } && _config.Updates.SkippedVersion == r.Version.ToString())
        {
            pnlHeader.Controls.Remove(_btnUpdate);
            _btnUpdate.Dispose();
            _btnUpdate = null;
            PositionHeader();
        }
    }
}
