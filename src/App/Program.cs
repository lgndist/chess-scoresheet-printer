namespace NotasyonOtomasyonu.App;

internal static class Program
{
    /// <summary>Güncellemeyle açıldıysa önceki sürümün numarası ("--updated 1.5.2"); yoksa null.</summary>
    internal static string? UpdatedFrom { get; private set; }

    /// <summary>Uygulama giriş noktası.</summary>
    [STAThread]
    private static void Main(string[] args)
    {
        // Güncellemeden sonra: eski sürüm kapanana kadar bekle (config.json'u ikisi aynı anda yazmasın), eski exe'yi sil.
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--updated") UpdatedFrom = args[i + 1];
            if (args[i] == "--wait" && int.TryParse(args[i + 1], out var pid))
                try { System.Diagnostics.Process.GetProcessById(pid).WaitForExit(15000); } catch { /* çoktan kapandı */ }
        }
        Update.Updater.CleanupOld();
        AppPaths.Init(AppContext.BaseDirectory);   // ayarlar ve kullanıcı dosyaları: %APPDATA%\ChessScoresheetPrinter

        // WinForms yüksek DPI ve varsayılan font ayarları.
        ApplicationConfiguration.Initialize();

        // İkinci pencere açılmasın: ikisi aynı ayar dosyasını yazarsa biri ötekinin değişikliklerini ezer.
        if (!AppPaths.AcquireSingleInstance(afterUpdate: UpdatedFrom is not null)) return;

        // Beklenmeyen hatalarda uygulama çökmesin, anlaşılır mesaj göster.
        Application.ThreadException += (_, e) =>
            MessageBox.Show("Beklenmeyen bir hata oluştu:\n\n" + e.Exception.Message,
                "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

        Application.Run(new MainForm());
    }
}
