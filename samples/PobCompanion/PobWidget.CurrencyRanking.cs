using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    internal static (string Realm,string League) ListingMarket(string url,int game)
    {
        var parts=new Uri(url).AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);
        var index=Array.IndexOf(parts,"search")+1;
        if(index<=0||index>=parts.Length)throw new IOException("The listing league is unavailable. Open a trade search again.");
        var realm=PobGame.For(game).DefaultRealm;
        if(parts[index] is "pc" or "poe2" or "xbox" or "sony")realm=parts[index++];
        if(index>=parts.Length)throw new IOException("The listing league is unavailable.");
        return(realm,Uri.UnescapeDataString(parts[index]));
    }
    internal Func<string,string,CancellationToken,Task<CurrencyRateSnapshot>>? CurrencyRateLoader {get;init;}
    private async Task<(RankedItem[] Rows,CurrencyRateSnapshot? Rates)> SortRankingAsync(RankedItem[] rows,string mode,string xml,CancellationToken token)
    {
        if(rows.Length==0)return(rows,null);
        CurrencyRateSnapshot? rates=null;
        if(mode!="strength")
        {
            if(rows.Any(r=>r.Candidate.Listing.PriceAmount is null||r.Candidate.Listing.PriceCurrency is null))
                throw new IOException("Some saved listings have no numeric price. Load fresh results before sorting by price. Existing results are unchanged.");
            var market=ListingMarket(rows[0].Candidate.Listing.Url,game);
            if(rows.Any(r=>ListingMarket(r.Candidate.Listing.Url,game)!=market))throw new IOException("Price sorting requires listings from the same realm and league.");
            rates=CurrencyRateLoader is {} loader?await loader(market.Realm,market.League,token):await PobCurrencyRates.ReadAsync(game,market.Realm,market.League,dataDirectory,engine,xml,token);
        }
        var reply=await engine.ExecuteAsync(new{op="trade-rank-sort",mode,rates=rates?.Rates,
            rows=rows.Select(r=>new{score=r.Evaluation.GetProperty("score").GetDouble(),amount=r.Candidate.Listing.PriceAmount,currency=r.Candidate.Listing.PriceCurrency}).ToArray()},token,xml);
        var indices=A(reply.State,"order").Select(i=>i.GetInt32()-1).ToArray();
        if(indices.Length!=rows.Length||indices.Distinct().Count()!=rows.Length||indices.Any(i=>i<0||i>=rows.Length))throw new IOException("PoB returned incomplete sorted results.");
        return(indices.Select(i=>rows[i]).ToArray(),rates);
    }
    private string RankingRateLabel()
    {
        if(rankingSort=="strength")return "Ordered by build improvement; price does not affect this order.";
        if(rankingRates is not {} rates||!rates.Valid(rates.Realm,rates.League,DateTimeOffset.UtcNow))return "Exchange rates unavailable. Choose an order to refresh.";
        var age=DateTimeOffset.UtcNow-rates.FetchedAt;
        return $"GGG · {rates.League} · trades {rates.MarketHour.ToLocalTime():g}–{rates.MarketHour.AddHours(1).ToLocalTime():t} · fetched {Math.Max(0,(int)age.TotalMinutes)} min ago"+
            (age>=TimeSpan.FromMinutes(61)?" · choose an order to refresh":"")+
            (rankingSort=="value"?". PoB balances improvement with a logarithmic price penalty.":". Prices converted to Divine equivalents.");
    }
}
