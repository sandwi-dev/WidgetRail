using System.Text.Json;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PobCompanion;

internal sealed record PobTradeListing(string Id,string Url,string Price,string Seller,string Method,string? Fee,string? Availability=null,string? Character=null)
{
    internal bool IsValid(int game)=>Id is {Length:>=16 and <=128}&&Id.All(char.IsAsciiHexDigit)&&
        Url is {Length:<=8192}&&Uri.TryCreate(Url,UriKind.Absolute,out var uri)&&uri.UserInfo.Length==0&&
        uri.GetLeftPart(UriPartial.Authority)=="https://www.pathofexile.com"&&uri.AbsolutePath.StartsWith(game==2?"/trade2/":"/trade/",StringComparison.Ordinal)&&
        Price is {Length:<=100}&&Seller is {Length:<=100}&&Method is {Length:<=60}&&(Fee is null||Fee.Length<=120)&&
        (Availability is null||Availability.Length<=80)&&(Character is null||Character.Length<=100);
}

public sealed partial class PobWidget
{
    private PobTradeListing? tradeListing;
    private async Task ReadWebsiteActionsAsync(WidgetWebSession session)
    {
        try
        {
            await foreach(var message in session.ReadMessagesAsync(WidgetLifetimeToken))
            {
                lock(gate)
                {
                    if(!ReferenceEquals(websiteSession,session))return;
                    if(S(message,"kind")=="page-action-error")
                    {websiteVisible=false;ShowFeedback("Website comparison unavailable","Reload the results or use Paste item to compare.",ToastTone.Warning);Invalidate();continue;}
                    if(S(message,"kind")!="page-action"||S(message,"action")!="compare-item")continue;
                    if(busy||pendingBefore is not null||pastedItem is not null||result is null)
                    {websiteVisible=false;ShowFeedback("Finish current work",result is null?"Open a build before comparing an item.":"Apply or discard the current comparison or item draft first.",ToastTone.Warning);Invalidate();continue;}
                    if(!message.TryGetProperty("payload",out var payload)||payload.ValueKind!=JsonValueKind.Object)
                    {websiteVisible=false;ShowFeedback("Item unavailable","The website returned incomplete item data. Reload the search.",ToastTone.Warning);Invalidate();continue;}
                    payload=payload.Clone();
                    websiteVisible=false;
                    Work(token=>ReadTradeItemAsync(payload,token),"Reading trade item…");
                }
            }
        }
        catch(Exception error)when(error is OperationCanceledException or WidgetCapabilityException or ObjectDisposedException or IOException or JsonException or InvalidOperationException)
        {
            if(!WidgetLifetimeToken.IsCancellationRequested)
            {lock(gate){if(ReferenceEquals(websiteSession,session))ShowFeedback("Website connection ended","Close and reopen the website to continue comparing items.",ToastTone.Warning);}Invalidate();}
        }
    }
    private async Task ReadTradeItemAsync(JsonElement payload,CancellationToken token)
    {
        var url=S(payload,"url");
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.GetLeftPart(UriPartial.Authority)!="https://www.pathofexile.com"||
            !uri.AbsolutePath.StartsWith(game==2?"/trade2/":"/trade/",StringComparison.Ordinal))throw new IOException("This listing belongs to a different game or website.");
        if(!payload.TryGetProperty("entry",out var entry)||entry.ValueKind!=JsonValueKind.Object||
            !entry.TryGetProperty("item",out var item)||item.ValueKind!=JsonValueKind.Object||
            !entry.TryGetProperty("listing",out var listing)||listing.ValueKind!=JsonValueKind.Object||
            !listing.TryGetProperty("account",out var account)||account.ValueKind!=JsonValueKind.Object)
            throw new IOException("The website returned incomplete item data. Reload the search.");
        var id=S(entry,"id");
        if(id.Length is <16 or >128||!id.All(char.IsAsciiHexDigit))throw new IOException("This listing has no valid identity. Reload the search.");
        var price=listing.TryGetProperty("price",out var p)?S(p,"amount")+" × "+CurrencyName(S(p,"currency")):"Price unavailable";
        var context=new PobTradeListing(id,url,Limit(price,100),Limit(S(account,"name"),100),Limit(S(listing,"method"),60),
            listing.TryGetProperty("fee",out var fee)&&fee.ValueKind==JsonValueKind.Number?Limit(fee.ToString(),120):null,
            account.TryGetProperty("online",out var online)&&online.ValueKind==JsonValueKind.Object
                ?S(online,"status")=="afk"?"Seller was away when results loaded":"Seller was online when results loaded":null,Limit(S(account,"lastCharacterName"),100));
        if(!context.IsValid(game))throw new IOException("The listing details are incomplete. Reload the search.");
        var before=result!;
        var converted=await engine.ExecuteAsync(new{op="trade-item",entry},token,before.Xml);
        var raw=S(converted.State,"raw");
        var slots=await ItemSlotsAsync(raw,before.Xml,token);
        if(slots.Length==0)throw new IOException("This item has no compatible equipment slot in the current build.");
        token.ThrowIfCancellationRequested();
        SaveWork(before,new(before.Xml,undo.ToArray(),redo.ToArray(),ItemDraft:raw,TradeListing:context),refreshXml:false);
        lock(gate)
        {tradeListing=context;pastedItem=raw;itemDraftSlots=slots;detailKind="choose-slot";section="Items";websiteVisible=false;focusRequestSequence++;}
    }
    private void AddTradeListing(List<WidgetElement> rows)
    {
        if(tradeListing is not {} listing)return;
        rows.Add(Text(listing.Price+" · "+listing.Seller,"trade.listing","pob-heading"));
        if(listing.Method=="psapi")rows.Add(Text("In-person trade","trade.method","pob-small pob-muted"));
        if(listing.Availability is not null)rows.Add(Text(listing.Availability,"trade.availability","pob-small pob-muted"));
        if(listing.Fee is not null)rows.Add(Text("Gold fee: "+listing.Fee,"trade.fee","pob-small pob-muted"));
        rows.Add(Text("Listed price may change. Return to the official listing to check availability and buy.","trade.purchase.help","pob-small pob-muted"));
        rows.Add(websiteSession is not null?Button("Return to listing","website-resume"):WebsiteButton("Reopen trade search","trade-listing-open",listing.Url));
    }
    private static string CurrencyName(string currency)=>currency switch
    {"chaos"=>"Chaos Orb","divine"=>"Divine Orb","exalted"=>"Exalted Orb","alch"=>"Orb of Alchemy","annul"=>"Orb of Annulment","mirror"=>"Mirror of Kalandra",_=>currency};
}
