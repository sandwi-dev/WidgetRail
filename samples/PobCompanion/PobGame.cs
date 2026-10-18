namespace WidgetRail.Samples.PobCompanion;

/// <summary>Fixed game identity and website contracts, independent of engine feature availability.</summary>
internal sealed record PobGame
{
    private PobGame(int id, string name, string documentRoot, string tradePath, string realm, bool compressedSearch, bool gameFileExport)
    {
        Id = id; Name = name; DocumentRoot = documentRoot; TradePath = tradePath;
        DefaultRealm = realm; CompressedSearch = compressedSearch; SupportsGameFileExport = gameFileExport;
    }

    internal int Id { get; }
    internal string Name { get; }
    internal string DocumentRoot { get; }
    internal string TradePath { get; }
    internal string TradeApiPath => "/api" + TradePath;
    internal string TradeSearchPath => TradePath + "search/";
    internal string DefaultRealm { get; }
    internal bool CompressedSearch { get; }
    internal bool SupportsGameFileExport { get; }
    internal string ItemCopyHelp => Id == 2
        ? "In the overlay browser, use Compare in PoB beside a listing. For clipboard import, hover over an item in game and press Ctrl+C. The PoE2 website has no built-in Copy item button in an external browser."
        : "In the overlay browser, use Compare in PoB beside a listing. On the trade site, use Copy item for clipboard import. In game, hover over an item and press Ctrl+C, then paste here.";

    internal bool SupportsRealm(string realm) => Id switch
    {
        1 => realm is "pc" or "xbox" or "sony",
        2 => realm == "poe2",
        _ => false
    };

    private static readonly PobGame Poe1 = new(1, "PoE1 Path of Building (Beta)", "PathOfBuilding", "/trade/", "pc", false, false);
    private static readonly PobGame Poe2 = new(2, "PoE2 Path of Building (Beta)", "PathOfBuilding2", "/trade2/", "poe2", true, true);
    internal static PobGame For(int id) => id switch
    {
        1 => Poe1,
        2 => Poe2,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "A supported Path of Exile game is required.")
    };
}
