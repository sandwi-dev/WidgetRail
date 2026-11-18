using WidgetRail.Samples.DiscordWebView.Domain;

internal static class MessageTextTests
{
    internal static void Run(Action<bool,string> check)
    {
        var formatted=DiscordMessageText.Parse("**bold** *italic* __under__ ~~gone~~ `x * y` 👨‍👩‍👧‍👦");
        check(formatted.Any(part=>part.Text=="bold" && part.Style.HasFlag(DiscordTextStyle.Bold)) &&
            formatted.Any(part=>part.Text=="italic" && part.Style.HasFlag(DiscordTextStyle.Italic)) &&
            formatted.Any(part=>part.Text=="under" && part.Style.HasFlag(DiscordTextStyle.Underline)) &&
            formatted.Any(part=>part.Text=="gone" && part.Style.HasFlag(DiscordTextStyle.Strike)),"Message emphasis projects native text styles");
        check(formatted.Any(part=>part.Text=="x * y" && part.Style.HasFlag(DiscordTextStyle.Code)) &&
            string.Concat(formatted.Select(part=>part.Text)).Contains("👨‍👩‍👧‍👦"),"Code preserves literal markup and Unicode emoji sequences");
        var concealed=DiscordMessageText.Parse("before ||secret https://secret.example|| after");
        check(concealed.Any(part=>part.Style.HasFlag(DiscordTextStyle.Spoiler)) && concealed.All(part=>!part.Text.Contains("secret") && part.Url is null),
            "Hidden spoilers expose neither text nor link preview");
        check(DiscordMessageText.Parse("||secret||",revealSpoilers:true).Single().Text=="secret","Explicit spoiler reveal restores content");
        check(DiscordMessageText.Parse("start ||secret cut off",contentTruncated:true).All(part=>!part.Text.Contains("secret")),
            "Truncation before closing spoiler cannot expose hidden content");
        var names=DiscordMessageText.Parse("<@123> <:wave:456> <a:dance:789>",[new("123","Sanky")]);
        check(names.Any(part=>part.Text=="@Sanky") && names.Count(part=>part.EmojiId is not null)==2,
            "Mentions use provider names and custom emoji retain bounded identities");
        var links=DiscordMessageText.Parse("[Open](https://example.test/path) https://example.test/ok. [bad](javascript:alert(1))");
        check(links.Count(part=>part.Url is not null)==2 && links.Where(part=>part.Url is not null).All(part=>part.Url!.StartsWith("https://")) &&
            links.Any(part=>part.Url=="https://example.test/ok"),"Message links reject script targets and exclude trailing punctuation");
        check(DiscordMessageText.Parse(@"\*literal\*").Single().Text=="*literal*","Escaped markdown remains literal");
        var many=DiscordMessageText.Parse(string.Concat(Enumerable.Repeat("**x** *y* ",200))+"||hidden||");
        check(many.Count<=128 && many.All(part=>!part.Text.Contains("hidden")),"Formatting budget remains bounded without exposing later spoilers");
        check(DiscordMessageText.Parse("> quoted\n```cs\nvar x = 1;\n```").Any(part=>part.Style.HasFlag(DiscordTextStyle.Quote)) &&
            DiscordMessageText.Parse("```cs\nvar x = 1;\n```").Single().Text=="var x = 1;\n","Quotes and fenced code remain readable");
        check(DiscordMessageText.Parse("***both***").Single() is {Text:"both"} combined&&combined.Style==(DiscordTextStyle.Bold|DiscordTextStyle.Italic)&&
            DiscordMessageText.Parse("**bold *italic***").Any(part=>part.Text=="italic"&&part.Style.HasFlag(DiscordTextStyle.Bold|DiscordTextStyle.Italic)),
            "Combined and nested emphasis preserve both native styles");
    }
}
