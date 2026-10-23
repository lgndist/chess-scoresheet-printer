using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

/// <summary>Pano listeleri için: tur başı puanlar, tur sonrası sıralama, sonucu girilmiş son tur.</summary>
public class ListTests
{
    private static string Load(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name), System.Text.Encoding.UTF8);

    [Fact]
    public void Pairings_ReadPointsBeforeRound()
    {
        var t = ChessResultsParser.ParsePairings(Load("pairings_points_rd4.html"), 1510309, 4);
        var b1 = t.Pairings.First(p => p.Board == 1);
        Assert.Equal("DEMİRER, ELİF", b1.White.Name);
        Assert.Equal("2½", b1.WhitePoints);
        Assert.Equal("2½", b1.BlackPoints);
        Assert.Equal("1 - 0", b1.Result);
    }

    [Fact]
    public void Standings_TitleColumnsAndRows()
    {
        var st = ChessResultsParser.ParseStandings(Load("standings_rd4.html"))!;
        Assert.Equal("4. Turdan Sonra Sıralama", st.Title);
        Assert.Contains("İsim", st.Headers);
        Assert.Contains("Puan", st.Headers);
        Assert.DoesNotContain("FED", st.Headers);   // panoda gereksiz
        Assert.DoesNotContain("", st.Headers);      // bayrak sütunu (metinsiz) atılır
        Assert.Equal(10, st.Rows.Count);
        var first = st.Rows[0];
        Assert.Equal("DEMİRER, ELİF", first[st.Headers.ToList().IndexOf("İsim")]);
        Assert.Equal("3,5", first[st.Headers.ToList().IndexOf("Puan")]);
    }

    [Fact]
    public void EventInfo_RankedRoundFromStandingsTitle()
    {
        Assert.Equal(4, ChessResultsParser.ParseEventInfo(Load("standings_rd4.html"), 1510309).RankedRound);
        // Turnuva başlamadan: "Başlangıç Sıralaması" → 0
        Assert.Equal(0, ChessResultsParser.ParseEventInfo(Load("event_not_started.html"), 1).RankedRound);
    }
}
