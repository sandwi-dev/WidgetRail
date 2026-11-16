using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace WidgetRail.Playnite;

[DataContract]
public sealed class ProviderCaptureContext
{
    [DataMember(Name = "idempotencyKey", Order = 0)] public string IdempotencyKey { get; set; } = "";
    [DataMember(Name = "references", Order = 1)] public ProviderCaptureReference[] References { get; set; } = Array.Empty<ProviderCaptureReference>();
    [DataMember(Name = "eventAtUnixMilliseconds", Order = 2, EmitDefaultValue = false)] public long? EventAtUnixMilliseconds { get; set; }
}
[DataContract]
public sealed class ProviderCaptureReference
{
    [DataMember(Name = "role", Order = 0)] public string Role { get; set; } = "";
    [DataMember(Name = "kind", Order = 1)] public string Kind { get; set; } = "";
    [DataMember(Name = "id", Order = 2)] public string Id { get; set; } = "";
    [DataMember(Name = "label", Order = 3)] public string Label { get; set; } = "";
    [DataMember(Name = "hasRelatedAction", Order = 4, EmitDefaultValue = false)] public bool HasRelatedAction { get; set; }
}

/// <summary>Conservative reader for the provider's documented v4.1 capture naming format.
/// Only this domain adapter interprets game/achievement names; the host reads generic companions.</summary>
public static class ProviderCaptureCompanions
{
    public const string Suffix = ".widgetrail-context.json";
    private static readonly Regex Number = new Regex(@"^\d{3,5}_(.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex Duplicate = new Regex(@" \(\d+\)$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Reserved = new HashSet<string>(new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }, StringComparer.OrdinalIgnoreCase);
    public static string Segment(string value, bool game = false)
    {
        var output = new StringBuilder(); var underscore = false;
        foreach (var c in (value ?? "").Trim())
        {
            var invalid = char.IsControl(c) || "<>:\"/\\|?*".IndexOf(c) >= 0;
            if (invalid)
            {
                if (!game && !underscore) { output.Append('_'); underscore = true; }
                continue;
            }
            output.Append(c); underscore = false;
        }
        var text = output.ToString().Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(text)) text = "achievement";
        if (Reserved.Contains(text)) text += "_";
        return text.Length <= 96 ? text : text.Substring(0,96).TrimEnd('.', ' ');
    }
    public static string? AchievementStem(string fileName, IEnumerable<string> configuredSuffixes)
        => AchievementStems(fileName,configuredSuffixes).FirstOrDefault();
    public static bool HasCaptureSignature(string fileName)=>FileStem(fileName)!=null;
    private static string? FileStem(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.Equals(extension,".png",StringComparison.OrdinalIgnoreCase) && !string.Equals(extension,".mp4",StringComparison.OrdinalIgnoreCase)) return null;
        var match = Number.Match(Duplicate.Replace(Path.GetFileNameWithoutExtension(fileName), ""));
        if (!match.Success) return null;
        return match.Groups[1].Value;
    }
    public static IReadOnlyList<string> AchievementStems(string fileName,IEnumerable<string> configuredSuffixes)
    {
        var stem=FileStem(fileName);if(stem==null)return Array.Empty<string>();
        var configured=configuredSuffixes.Take(8).ToArray();var matches=new List<string>();
        foreach(var suffix in configured.Where(value=>!string.IsNullOrWhiteSpace(value)).Select(value=>"_"+Segment(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(value=>value.Length))
            if(stem.Length>suffix.Length&&stem.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))matches.Add(stem.Substring(0,stem.Length-suffix.Length));
        if(configured.Any(string.IsNullOrWhiteSpace))matches.Add(stem);
        return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static T? Unique<T>(IEnumerable<T> values) where T : class
    {
        using (var iterator = values.GetEnumerator())
        { if (!iterator.MoveNext()) return null; var value = iterator.Current; return iterator.MoveNext() ? null : value; }
    }
    public static byte[] Encode(ProviderCaptureContext context)
    {
        using (var stream = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(ProviderCaptureContext)).WriteObject(stream,context);
            if (stream.Length > 4096) throw new InvalidDataException("Capture context is too large.");
            return stream.ToArray();
        }
    }
    public static bool Write(string root, string mediaPath, ProviderCaptureContext context)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        mediaPath = Path.GetFullPath(mediaPath);
        if (!mediaPath.StartsWith(root + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !File.Exists(mediaPath)) return false;
        for (var path = mediaPath; path != null; path = Path.GetDirectoryName(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            if (string.Equals(path,root,StringComparison.OrdinalIgnoreCase)) break;
        }
        var companion = mediaPath + Suffix;
        // Never replace a different association or an unrecognized external
        // companion. Safe same-association updates preserve the user's visibility.
        FileAttributes? existingAttributes=null;
        if (File.Exists(companion))
        {
            existingAttributes=File.GetAttributes(companion);
            if ((existingAttributes.Value&FileAttributes.ReparsePoint)!=0||new FileInfo(companion).Length>8192) return false;
            ProviderCaptureContext? existing;
            try { existing=IntegrationFiles.Read<ProviderCaptureContext>(companion); }
            catch(SerializationException) { return false; }
            if(!SameAssociation(existing,context)) return false;
            if(Encode(existing!).SequenceEqual(Encode(context)))return false;
        }
        var bytes = Encode(context); var temporary = companion + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
            File.SetAttributes(temporary,existingAttributes??(File.GetAttributes(temporary)|FileAttributes.Hidden));
            if (!File.Exists(mediaPath)) return false;
            if(existingAttributes.HasValue)
            {
                // Refuse a concurrently replaced association; an expected path
                // alone is not authority to overwrite another writer's metadata.
                var current=IntegrationFiles.Read<ProviderCaptureContext>(companion);
                if(!SameAssociation(current,context))return false;
                var attributes=File.GetAttributes(companion);
                if((attributes&FileAttributes.ReparsePoint)!=0)return false;
                File.SetAttributes(temporary,attributes);
                File.Replace(temporary,companion,null);
            }
            else
            {
                if(File.Exists(companion))return false;
                File.Move(temporary,companion);
            }
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static bool SameAssociation(ProviderCaptureContext? first,ProviderCaptureContext second)=>first!=null&&first.IdempotencyKey==second.IdempotencyKey&&
        first.References!=null&&first.References.Length==second.References.Length&&!first.References.Where((reference,index)=>reference==null||
            reference.Role!=second.References[index].Role||reference.Kind!=second.References[index].Kind||reference.Id!=second.References[index].Id).Any();
}
