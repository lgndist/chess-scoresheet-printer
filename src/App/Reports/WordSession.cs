using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace NotasyonOtomasyonu.App.Reports;

/// <summary>
/// Düzenleme ekranı açıkken Word'ü TEK kez başlatıp açık tutar: her düzenlemeden sonra belge yeniden
/// PDF'e çevrilirken Word'ün açılış süresi (ilk açılışta 10–40 sn) tekrar ödenmez. İşler sırayla
/// tek bir STA iş parçacığında koşar; Dispose Word'ü kapatır.
/// </summary>
public sealed class WordSession : IDisposable
{
    private const int WdExportFormatPdf = 17;
    private readonly BlockingCollection<Action> _queue = new();
    private dynamic? _app;
    private readonly Thread _thread;

    public WordSession()
    {
        _thread = new Thread(() =>
        {
            try { foreach (var job in _queue.GetConsumingEnumerable()) job(); }
            finally { Quit(); }
        }) { IsBackground = true, Name = "WordSession" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Word'ü arka planda şimdiden başlatır (ilk önizleme beklemesi kısalsın).</summary>
    public Task WarmUpAsync() => Run(() => App());

    public Task ToPdfAsync(string docxPath, string pdfPath) => Run(() =>
    {
        try { Export(docxPath, pdfPath); }
        catch (COMException)
        {
            // Word kapanmış/çökmüş olabilir: yeniden başlatıp bir kez daha dene.
            Quit();
            Export(docxPath, pdfPath);
        }
    });

    private void Export(string docxPath, string pdfPath)
    {
        dynamic doc = App().Documents.Open(Path.GetFullPath(docxPath), false, true); // ConfirmConversions, ReadOnly
        try { doc.ExportAsFixedFormat(pdfPath, WdExportFormatPdf); }
        finally { doc.Close(false); Marshal.FinalReleaseComObject(doc); }
    }

    private dynamic App()
    {
        if (_app is not null) return _app;
        var type = Type.GetTypeFromProgID("Word.Application")
                   ?? throw new InvalidOperationException("Bu bilgisayarda Microsoft Word bulunamadı.");
        _app = Activator.CreateInstance(type)!;
        _app.Visible = false;
        _app.DisplayAlerts = 0;
        return _app;
    }

    private void Quit()
    {
        if (_app is null) return;
        try { _app.Quit(false); } catch { /* zaten kapanmış olabilir */ }
        try { Marshal.FinalReleaseComObject(_app); } catch { }
        _app = null;
    }

    private Task Run(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                try { action(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
        }
        catch (InvalidOperationException) { tcs.SetException(new ObjectDisposedException(nameof(WordSession))); }
        return tcs.Task;
    }

    /// <summary>
    /// Kuyruğu kapatır; Word, sıradaki iş bitince kapatılır. Arayüz en çok 3 sn bekler (Word ilk açılışta
    /// yavaşsa pencere donmasın); bekleme aşılırsa Word arka planda kapanmaya devam eder.
    /// </summary>
    public void Dispose()
    {
        _queue.CompleteAdding();
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(3));
    }
}
