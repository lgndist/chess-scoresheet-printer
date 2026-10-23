using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Registration;

/// <summary>Kendiliğinden eşitlemenin sonucu (günlüğe ve durum satırına yazılır).</summary>
public sealed record RegistrationSyncResult(bool Changed, string Message, EventRegistration? Registration);

/// <summary>
/// Turnuva açılınca kayıt listesini kendiliğinden getirir: o turnuvanın TSF kayıt formu en eksiksiz
/// kaynaktır (il ve kategori sayıları buradan alınır). Form sporcu örtüşmesiyle bulunur; liste
/// <see cref="RefreshAfter"/> süresinden eskiyse yenilenir (kayıtlar son başvuruya kadar artar).
/// Site erişilemezse ya da form kapanmışsa eldeki liste asla silinmez.
/// </summary>
public static class RegistrationSync
{
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(6);

    public static async Task<RegistrationSyncResult> EnsureAsync(
        AppConfig cfg, ChessResultsService online, int eventTnr, string eventName, string? province,
        IReadOnlyList<CategoryRef> categories, bool force = false, CancellationToken ct = default)
    {
        string key = eventTnr.ToString();
        var existing = ProvinceBook.For(cfg, eventTnr);
        if (existing is not { Entries.Count: > 0 } && ProvinceBook.ByTitle(cfg, eventName) is { } byTitle)
        {
            // Liste başka bir turnuva açıkken alınmış: başlığı bu turnuvaya uyuyor → bu turnuvaya bağla.
            existing = Copy(byTitle, eventName, existing);
            cfg.Registrations[key] = existing;
        }
        if (!force && existing is { Entries.Count: > 0 } && DateTime.Now - existing.FetchedAt < RefreshAfter)
            return new(false, "", existing);

        var site = existing is { Site.Length: > 0 } ? existing.Site : TsfSites.SiteFor(province, cfg.TsfSites);
        if (string.IsNullOrWhiteSpace(site))
            return new(false, "Kayıt listesi: turnuvanın ili bilinmediği için TSF sitesi bulunamadı (Ayarlar → Varsayılan il).", existing);

        // Bu turnuvanın sporcuları (formu bulmak için)
        var roster = new List<RosterPlayer>();
        foreach (var c in categories)
        {
            try { roster.AddRange((await online.GetStartingListAsync(c.Tnr, ct)).Select(p => new RosterPlayer(p.Name, p.Rating, c.Name))); }
            catch { /* kategori atlanır */ }
        }

        using var sites = new TsfSites();
        var pages = new List<RegistrationPage>();
        try
        {
            var opts = await sites.ListOpenAsync(site, ct);
            // Daha önce bağlanmış formlar sitede artık listelenmese de (kayıt kapanmış) denenir.
            var tids = opts.Select(o => (o.Tid, o.Title)).ToList();
            foreach (var (tid, title) in existing?.Tids.Select((t, i) => (t, i < existing.Titles.Count ? existing.Titles[i] : "")) ?? Enumerable.Empty<(int, string)>())
                if (tids.All(x => x.Tid != tid)) tids.Add((tid, title));

            foreach (var (tid, title) in tids)
            {
                bool known = existing?.Tids.Contains(tid) == true;
                double titleScore = RegistrationMatch.TitleScore(title, eventName);
                // Ne bağlı ne de adı biraz olsun benzer form için sayfa indirmeye gerek yok (sporcu listesi yoksa).
                if (!known && roster.Count == 0 && titleScore < 0.5) continue;
                var page = await sites.GetPageAsync(site, tid, ct);
                if (page is null) continue;
                int matched = RegistrationMatch.Overlap(roster, page.Entries);
                if (known || RegistrationMatch.IsLikely(titleScore, matched, page.Entries.Count, roster.Count))
                    pages.Add(page);
            }
        }
        catch (Exception ex)
        {
            return new(false, "Kayıt listesi alınamadı: " + Friendly(ex) + (existing is { Entries.Count: > 0 } ? " Eldeki liste kullanılıyor." : ""), existing);
        }

        if (pages.Count == 0)
            return new(false, existing is { Entries.Count: > 0 }
                ? "Kayıt listesi yenilenemedi (form kapanmış olabilir); eldeki liste kullanılıyor."
                : "TSF sitesinde bu turnuvaya ait kayıt formu bulunamadı (📋 Kayıt / İl ile elle seçilebilir).", existing);

        var reg = new EventRegistration
        {
            Site = site!, EventName = eventName, FetchedAt = DateTime.Now,
            Overrides = existing?.Overrides ?? new(), Resolved = existing?.Resolved ?? new()
        };
        foreach (var p in pages)
        {
            reg.Tids.Add(p.Tid);
            reg.Titles.Add(p.Title);
            reg.Entries.AddRange(p.Entries);
        }
        // Yeniden okunamayan (kapanmış) formların eski kayıtları korunur.
        if (existing is not null)
            for (int i = 0; i < existing.Tids.Count; i++)
                if (!reg.Tids.Contains(existing.Tids[i]))
                {
                    reg.Tids.Add(existing.Tids[i]);
                    reg.Titles.Add(i < existing.Titles.Count ? existing.Titles[i] : "");
                    reg.Entries.AddRange(existing.Entries.Where(e => e.Tid == existing.Tids[i]));
                }
        // Daha az kayıt geldiyse (sayfa kısmen okundu) eskiyi tercih et: liste küçülmez.
        if (existing is { Entries.Count: > 0 } && reg.Entries.Count < existing.Entries.Count * 0.8)
            return new(false, "Kayıt listesi yenilenirken eksik okundu; eldeki liste kullanılıyor.", existing);

        int before = existing?.Entries.Count ?? 0;
        cfg.Registrations[key] = reg;
        var counts = string.Join(" · ", RegistrationCategories.Counts(reg).Select(c => $"{c.Name} {c.Count}"));
        var what = before == 0 ? "alındı" : reg.Entries.Count != before ? $"güncellendi ({before} → {reg.Entries.Count})" : "kontrol edildi";
        return new(true, $"📋 Kayıt listesi {what}: {reg.Entries.Count} sporcu — {counts}", reg);
    }

    private static EventRegistration Copy(EventRegistration source, string eventName, EventRegistration? keep) => new()
    {
        Site = source.Site, EventName = eventName, Tids = source.Tids.ToList(), Titles = source.Titles.ToList(),
        FetchedAt = source.FetchedAt, Entries = source.Entries, Overrides = keep?.Overrides ?? new()
    };

    private static string Friendly(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is System.Net.Sockets.SocketException) return "TSF sitesine ulaşılamıyor (site kapalı ya da internet yok).";
        return ex is TaskCanceledException ? "TSF sitesi yanıt vermedi." : ex.Message;
    }
}
