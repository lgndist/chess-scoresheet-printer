using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// chess-results eşleştirme tablosunun sütunlarını başlık adlarından çözer.
/// Tabloda "Rtg", "No.", "Puan" başlıkları İKİ kez geçer (beyaz/siyah); bu yüzden
/// "Sonuç" sütunu eksen alınır: solundakiler beyaz, sağındakiler siyah.
/// Böylece unvan/kulüp/bayrak gibi ek sütunlara karşı dayanıklıdır.
/// </summary>
internal sealed class PairingColumnMap
{
    private readonly int _board, _result;
    private readonly int _wName, _wRtg, _wNo;
    private readonly int _bName, _bRtg, _bNo;
    private readonly int _wPts, _bPts;
    private static readonly string[] Pts = { "puan", "pts.", "pts", "punkte", "pkt.", "points" };
    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static readonly string[] Board = { "masa", "bo.", "bo", "br.", "br", "board", "no" };
    private static readonly string[] Result = { "sonuç", "sonuc", "result", "res.", "res", "erg." };
    private static readonly string[] WhiteName = { "beyaz", "white", "name", "ad soyad", "isim" };
    private static readonly string[] BlackName = { "siyah", "black" };
    private static readonly string[] Rtg = { "rtg", "rtgi", "elo", "rating" };
    private static readonly string[] No = { "no.", "no", "sno", "sn", "sıra" };

    public PairingColumnMap(IReadOnlyList<string> headers)
    {
        _result = IndexOf(headers, Result);
        if (_result < 0) _result = headers.Count / 2; // emniyet: ortayı eksen al

        _board = IndexOf(headers, Board, before: _result, preferFirst: true);

        _wName = FirstIndex(headers, WhiteName, lo: 0, hi: _result);
        _bName = FirstIndex(headers, BlackName, lo: _result + 1, hi: headers.Count);

        _wRtg = FirstIndex(headers, Rtg, lo: 0, hi: _result);
        _bRtg = FirstIndex(headers, Rtg, lo: _result + 1, hi: headers.Count);

        // No. sütunları: beyaz solda, siyah sağda. "Masa" da No olabileceğinden board'ı dışla.
        _wNo = FirstIndexExcept(headers, No, lo: 0, hi: _result, except: _board);
        _bNo = FirstIndex(headers, No, lo: _result + 1, hi: headers.Count);

        _wPts = FirstIndex(headers, Pts, lo: 0, hi: _result);
        _bPts = FirstIndex(headers, Pts, lo: _result + 1, hi: headers.Count);

        // İsim başlıkla bulunamadıysa: eksenin solundaki/sağındaki en olası metin sütununu seç.
        if (_wName < 0) _wName = GuessNameColumn(headers, 0, _result);
        if (_bName < 0) _bName = GuessNameColumn(headers, _result + 1, headers.Count);
    }

    public Pairing? MapRow(IReadOnlyList<string> cells, int fallbackBoard)
    {
        string Cell(int i) => i >= 0 && i < cells.Count ? cells[i] : "";

        var whiteName = Cell(_wName).Trim();
        if (whiteName.Length == 0) return null; // boş/ayraç satırı

        var blackName = Cell(_bName).Trim();
        // "eşlendirilmeyenler" (not paired): oyuncu bu tur oynamıyor → kağıt yok.
        if (IsNotPaired(blackName)) return null;

        int board = ParseInt(Cell(_board)) ?? fallbackBoard;

        var white = new Player(
            StartNo: ParseInt(Cell(_wNo)),
            Name: whiteName,
            Rating: ParseRating(Cell(_wRtg)));

        Player? black = IsBye(blackName)
            ? null
            : new Player(StartNo: ParseInt(Cell(_bNo)), Name: blackName, Rating: ParseRating(Cell(_bRtg)));

        return new Pairing(board, white, black, Result_(Cell(_result)),
            WhitePoints: NullIfEmpty(Cell(_wPts)), BlackPoints: black is null ? null : NullIfEmpty(Cell(_bPts)));
    }

    // ---- index bulucular ----
    private static int IndexOf(IReadOnlyList<string> headers, string[] set)
    {
        for (int i = 0; i < headers.Count; i++)
            if (set.Contains(headers[i].ToLowerInvariant().Trim())) return i;
        return -1;
    }

    private static int IndexOf(IReadOnlyList<string> headers, string[] set, int before, bool preferFirst)
    {
        for (int i = 0; i < headers.Count && (before < 0 || i < before); i++)
            if (set.Contains(headers[i].ToLowerInvariant().Trim())) return i;
        return -1;
    }

    private static int FirstIndex(IReadOnlyList<string> headers, string[] set, int lo, int hi)
    {
        for (int i = Math.Max(0, lo); i < Math.Min(hi, headers.Count); i++)
            if (set.Contains(headers[i].ToLowerInvariant().Trim())) return i;
        return -1;
    }

    private static int FirstIndexExcept(IReadOnlyList<string> headers, string[] set, int lo, int hi, int except)
    {
        for (int i = Math.Max(0, lo); i < Math.Min(hi, headers.Count); i++)
            if (i != except && set.Contains(headers[i].ToLowerInvariant().Trim())) return i;
        return -1;
    }

    /// <summary>Başlık adı yoksa: aralıktaki boş başlıklı ya da en uzun metinli sütunu isim varsay.</summary>
    private static int GuessNameColumn(IReadOnlyList<string> headers, int lo, int hi)
    {
        // Önce boş başlıklı bir sütun (chess-results'ta isim sütunu kimi zaman başlıksızdır).
        for (int i = Math.Max(0, lo); i < Math.Min(hi, headers.Count); i++)
            if (headers[i].Trim().Length == 0) return i;
        return Math.Max(0, lo);
    }

    // ---- değer çözücüler ----
    // chess-results BAY'ı dile göre tek kelimeyle yazar (Türkçe sayfada "Tur").
    // Gerçek oyuncu adları "SOYAD, AD" biçiminde olduğundan TAM eşleşme yeterli ve güvenli;
    // "BAYTAR, ÇAĞAN" gibi adların BAY sanılmaması için içerik (Contains) araması yapılmaz.
    private static readonly HashSet<string> ByeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "tur", "bay", "bye", "-", "(=)", "spielfrei", "libre", "exempt"
    };

    private static bool IsBye(string name)
        => name.Length == 0 || ByeWords.Contains(name.Trim());

    /// <summary>"eşlendirilmeyenler" / "not paired" / "nicht ausgelost": oyuncu bu tur eşlenmedi.</summary>
    private static bool IsNotPaired(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        return n.StartsWith("eşlendirilmeyen") || n.StartsWith("eşlenmeyen") ||
               n is "not paired" or "nicht ausgelost" or "non apparié" or "no emparejado";
    }

    private static int? ParseInt(string s)
    {
        s = s.Trim();
        int end = 0;
        while (end < s.Length && char.IsDigit(s[end])) end++;
        return end > 0 && int.TryParse(s[..end], out var n) ? n : null;
    }

    private static int? ParseRating(string s)
    {
        var n = ParseInt(s);
        return n is null or 0 ? null : n;
    }

    private static string? Result_(string s)
    {
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }
}
