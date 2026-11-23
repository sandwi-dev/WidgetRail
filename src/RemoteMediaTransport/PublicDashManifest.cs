using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WidgetRail.RemoteMediaTransport;

/// <summary>A closed on-demand H.264/AAC DASH presentation. Only synthetic segment
/// addresses reach the native player. Original URLs stay in the transport map.</summary>
internal sealed class PublicDashManifest
{
    internal static readonly Uri PlaybackUri = new("https://widgetrail-media.invalid/store/manifest.mpd");
    private static readonly XNamespace Ns = "urn:mpeg:dash:schema:mpd:2011";
    private readonly Dictionary<Uri, Uri> segments = [];
    internal byte[] Manifest { get; private set; } = [];
    internal bool TryResolve(Uri address, out Uri? source) => segments.TryGetValue(address, out source);

    internal static PublicDashManifest Parse(Uri origin, ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > 128 * 1024) throw Invalid();
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 128 * 1024,
        });
        var input = XDocument.Load(reader).Root;
        if (input?.Name != Ns + "MPD" || Attr(input, "type") != "static" || input.Descendants(Ns + "ContentProtection").Any() ||
            input.Descendants(Ns + "BaseURL").Any() || input.Elements(Ns + "Period").Count() != 1) throw Invalid();
        var durationText = Attr(input, "mediaPresentationDuration");
        var seconds = XmlConvert.ToTimeSpan(durationText).TotalSeconds;
        if (seconds is <= 0 or > 600) throw Invalid();
        var period = new XElement(Ns + "Period", new XAttribute("id", "0"), new XAttribute("start", "PT0S"));
        var output = new XElement(Ns + "MPD", new XAttribute("type", "static"), new XAttribute("minBufferTime", "PT3S"),
            new XAttribute("profiles", "urn:mpeg:dash:profile:isoff-live:2011"), new XAttribute("mediaPresentationDuration", durationText), period);
        var result = new PublicDashManifest();
        var sourcePeriod = input.Element(Ns + "Period")!;
        var total = 0;
        var types = new HashSet<string>();
        foreach (var group in sourcePeriod.Elements(Ns + "AdaptationSet"))
        {
            var type = Attr(group, "contentType");
            if (type == "image") continue;
            if (type is not ("video" or "audio") || !types.Add(type)) throw Invalid();
            var target = new XElement(Ns + "AdaptationSet", new XAttribute("contentType", type),
                new XAttribute("id", types.Count), new XAttribute("segmentAlignment", "true"));
            foreach (var track in group.Elements(Ns + "Representation"))
            {
                if (++total > 12) throw Invalid();
                target.Add(result.ProjectTrack(origin, track, type, total, seconds));
            }
            if (!target.HasElements) throw Invalid();
            period.Add(target);
        }
        if (!types.Contains("video")) throw Invalid();
        result.Manifest = Encoding.UTF8.GetBytes(output.ToString(SaveOptions.DisableFormatting));
        return result;
    }

    private XElement ProjectTrack(Uri origin, XElement track, string type, int index, double seconds)
    {
        var id = Attr(track, "id");
        var codec = Attr(track, "codecs");
        var mime = Attr(track, "mimeType");
        if (!Regex.IsMatch(id, "^[0-9A-Za-z_-]{1,32}$", RegexOptions.CultureInvariant) ||
            mime != type + "/mp4" || (type == "audio" ? codec != "mp4a.40.2" :
                !Regex.IsMatch(codec, "^avc1[.][a-fA-F0-9]{6}$", RegexOptions.CultureInvariant))) throw Invalid();
        var rep = new XElement(Ns + "Representation", new XAttribute("id", index), new XAttribute("mimeType", mime),
            new XAttribute("codecs", codec), new XAttribute("bandwidth", Number(track, "bandwidth", 16000000)));
        if (type == "video")
        {
            rep.SetAttributeValue("width", Number(track, "width", 1920));
            rep.SetAttributeValue("height", Number(track, "height", 1080));
        }
        else
        {
            rep.SetAttributeValue("audioSamplingRate", Number(track, "audioSamplingRate", 96000));
            if (track.Element(Ns + "AudioChannelConfiguration") is { } channels)
                rep.Add(new XElement(channels.Name, new XAttribute("schemeIdUri", "urn:mpeg:dash:23003:3:audio_channel_configuration:2011"),
                    new XAttribute("value", Number(channels, "value", 8))));
        }
        var template = track.Element(Ns + "SegmentTemplate");
        if (template is null || template.HasElements) throw Invalid();
        var scale = Number(template, "timescale", 10000000);
        var duration = Number(template, "duration", 120000000);
        var start = Number(template, "startNumber", 1000000);
        var count = Math.Ceiling(seconds * scale / duration);
        if (count is < 1 or > 600 || (double)duration / scale > 12) throw Invalid();
        var init = Attr(template, "initialization").Replace("$RepresentationID$", id);
        var media = Attr(template, "media").Replace("$RepresentationID$", id);
        var format = media.Contains("$Number%05d$", StringComparison.Ordinal) ? "$Number%05d$" : "$Number$";
        if (!media.Contains(format, StringComparison.Ordinal)) throw Invalid();
        var prefix = "r" + index.ToString(CultureInfo.InvariantCulture) + "/";
        Add("init.m4s", init);
        for (var n = start; n < start + (int)count; n++)
            Add(n.ToString(CultureInfo.InvariantCulture) + ".m4s",
                media.Replace(format, n.ToString(format == "$Number$" ? "D" : "D5", CultureInfo.InvariantCulture)));
        rep.Add(new XElement(Ns + "SegmentTemplate", new XAttribute("timescale", scale), new XAttribute("duration", duration),
            new XAttribute("startNumber", start), new XAttribute("initialization", prefix + "init.m4s"),
            new XAttribute("media", prefix + "$Number$.m4s")));
        return rep;

        void Add(string key, string relative)
        {
            if (relative.Length > 256 || relative.Split('/').Any(part => part.Length == 0 || part is "." or ".." ||
                    part.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')))) throw Invalid();
            segments.Add(new Uri(PlaybackUri, prefix + key), new Uri(origin, relative));
        }
    }
    private static int Number(XElement item, string name, int limit) =>
        int.TryParse(Attr(item, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            value > 0 && value <= limit ? value : throw Invalid();
    private static string Attr(XElement item, string name) => (string?)item.Attribute(name) ?? throw Invalid();
    private static InvalidDataException Invalid() => new("Unsupported on-demand DASH manifest.");
}
