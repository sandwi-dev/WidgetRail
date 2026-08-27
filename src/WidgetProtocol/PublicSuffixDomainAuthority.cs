using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.WidgetProtocol;

internal sealed class PublicSuffixDomainAuthority
{
    private const string ResourceName = "WidgetRail.PublicSuffixList.dat";
    private const string ExpectedSha256 =
        "14EF61B1C212F701F3636C1D01AB9254DAF841F57EB6433BCBBEF56C726CA656";
    private const string ExpectedVersion = "// VERSION: 2026-08-19_19-18-48_UTC";
    private const string ExpectedCommit =
        "// COMMIT: e8c9a2b2b2856b6449999dd0ec0d118f364ed0cd";
    private static readonly Lazy<PublicSuffixDomainAuthority> Current = new(Load);

    private readonly HashSet<string> _exact = new(StringComparer.Ordinal);
    private readonly HashSet<string> _wildcard = new(StringComparer.Ordinal);
    private readonly HashSet<string> _exception = new(StringComparer.Ordinal);

    private PublicSuffixDomainAuthority() { }

    public bool Available { get; private set; }

    public static bool IsCanonicalRegistrableDomain(string value) =>
        Current.Value.IsRegistrableDomain(value);

    private bool IsRegistrableDomain(string value)
    {
        if (!Available || !TryCanonicalDnsName(value, out var canonical) ||
            !string.Equals(value, canonical, StringComparison.Ordinal))
            return false;
        return LabelCount(canonical) == PublicSuffixLabelCount(canonical) + 1;
    }

    private int PublicSuffixLabelCount(string domain)
    {
        var best = 1;
        for (var offset = 0; offset < domain.Length;)
        {
            var suffix = domain[offset..];
            if (_exception.Contains(suffix)) return LabelCount(suffix) - 1;
            if (_exact.Contains(suffix)) best = Math.Max(best, LabelCount(suffix));
            var dot = domain.IndexOf('.', offset);
            if (dot < 0) break;
            var wildcardBase = domain[(dot + 1)..];
            if (_wildcard.Contains(wildcardBase))
                best = Math.Max(best, LabelCount(wildcardBase) + 1);
            offset = dot + 1;
        }
        return best;
    }

    private static int LabelCount(string value) => value.Count(ch => ch == '.') + 1;

    private static bool TryCanonicalDnsName(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrEmpty(value) || value.Length > 253 ||
            value[0] == '.' || value[^1] == '.') return false;
        var labels = value.Split('.');
        if (labels.Any(label => label.Length is < 1 or > 63 ||
            label[0] == '-' || label[^1] == '-' ||
            label.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-')))
            return false;
        canonical = value.ToLowerInvariant();
        return true;
    }

    private static PublicSuffixDomainAuthority Load()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(ResourceName);
            if (stream is null) return new();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (bytes.Length != 333_164 ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)),
                    ExpectedSha256, StringComparison.Ordinal)) return new();
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (!text.Contains(ExpectedVersion, StringComparison.Ordinal) ||
                !text.Contains(ExpectedCommit, StringComparison.Ordinal)) return new();
            var authority = new PublicSuffixDomainAuthority();
            var idn = new IdnMapping { UseStd3AsciiRules = true };
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;
                var target = authority._exact;
                if (line[0] == '!')
                {
                    target = authority._exception;
                    line = line[1..];
                }
                else if (line.StartsWith("*.", StringComparison.Ordinal))
                {
                    target = authority._wildcard;
                    line = line[2..];
                }
                target.Add(idn.GetAscii(line).ToLowerInvariant());
            }
            if (authority._exact.Count < 5000 || authority._wildcard.Count == 0 ||
                authority._exception.Count == 0) return new();
            authority.Available = true;
            return authority;
        }
        catch
        {
            return new();
        }
    }
}
