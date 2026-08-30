using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal sealed record ExternalConsumerToolchain(
    string SdkVersion,
    IReadOnlyDictionary<string, string> InstalledPacks)
{
    private const string TargetFramework = "net8.0";
    private const string RuntimeIdentifier = "win-x64";
    private static readonly string[] RequiredTargetingPacks =
    [
        "Microsoft.NETCore.App.Ref",
        "Microsoft.WindowsDesktop.App.Ref",
        "Microsoft.AspNetCore.App.Ref",
    ];

    internal static async Task<ExternalConsumerToolchain> ConfigureAsync(
        string repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        var candidates = await DiscoverInstalledSdksAsync();
        foreach (var candidate in candidates.OrderByDescending(item => item.Version))
        {
            var toolchain = TryResolve(candidate);
            if (toolchain is null) continue;

            var configuration = new
            {
                sdk = new
                {
                    version = toolchain.SdkVersion,
                    rollForward = "disable",
                    allowPrerelease = false,
                },
            };
            await File.WriteAllTextAsync(
                Path.Combine(repository, "global.json"),
                JsonSerializer.Serialize(configuration, new JsonSerializerOptions
                {
                    WriteIndented = true,
                }) + Environment.NewLine);
            return toolchain;
        }

        throw new InvalidOperationException(
            "No installed .NET SDK has a complete local net8.0 reference-pack set. " +
            "Install or repair a supported .NET SDK before running the external-consumer verifier; " +
            "the verifier will not enable a network package source.");
    }

    private static async Task<IReadOnlyList<SdkCandidate>> DiscoverInstalledSdksAsync()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--list-sdks");
        using var process = Process.Start(start) ??
            throw new InvalidOperationException("Could not query installed .NET SDKs.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw new TimeoutException("Installed .NET SDK discovery exceeded 10 seconds.");
        }
        var standardOutput = await output;
        var standardError = await error;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Installed .NET SDK discovery failed with exit code {process.ExitCode}: " +
                standardError.Trim());

        var candidates = new List<SdkCandidate>();
        foreach (var line in standardOutput.Split(
                     ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line, @"^(?<version>[^\s]+) \[(?<root>.+)\]$");
            if (!match.Success ||
                !Version.TryParse(match.Groups["version"].Value, out var version))
                continue;
            var sdkRoot = Path.GetFullPath(match.Groups["root"].Value);
            var sdkDirectory = Path.Combine(sdkRoot, match.Groups["version"].Value);
            if (!Directory.Exists(sdkDirectory)) continue;
            candidates.Add(new(version, match.Groups["version"].Value, sdkDirectory));
        }
        return candidates;
    }

    private static ExternalConsumerToolchain? TryResolve(SdkCandidate candidate)
    {
        var manifestPath = Path.Combine(
            candidate.SdkDirectory, "Microsoft.NETCoreSdk.BundledVersions.props");
        if (!File.Exists(manifestPath)) return null;

        XDocument manifest;
        try
        {
            manifest = XDocument.Load(manifestPath, LoadOptions.None);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }

        var packVersions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var packName in RequiredTargetingPacks)
        {
            var versions = manifest.Descendants("KnownFrameworkReference")
                .Where(element =>
                    string.Equals((string?)element.Attribute("TargetFramework"),
                        TargetFramework, StringComparison.Ordinal) &&
                    string.Equals((string?)element.Attribute("TargetingPackName"),
                        packName, StringComparison.Ordinal))
                .Select(element => (string?)element.Attribute("TargetingPackVersion"))
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (versions.Length != 1) return null;
            packVersions.Add(packName, versions[0]!);
        }

        var appHost = manifest.Descendants("KnownAppHostPack")
            .SingleOrDefault(element =>
                string.Equals((string?)element.Attribute("Include"),
                    "Microsoft.NETCore.App", StringComparison.Ordinal) &&
                string.Equals((string?)element.Attribute("TargetFramework"),
                    TargetFramework, StringComparison.Ordinal) &&
                ((string?)element.Attribute("AppHostRuntimeIdentifiers"))?
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(RuntimeIdentifier, StringComparer.Ordinal) == true);
        var appHostPattern = (string?)appHost?.Attribute("AppHostPackNamePattern");
        var appHostVersion = (string?)appHost?.Attribute("AppHostPackVersion");
        if (string.IsNullOrWhiteSpace(appHostPattern) ||
            string.IsNullOrWhiteSpace(appHostVersion)) return null;
        var appHostName = appHostPattern.Replace(
            "**RID**", RuntimeIdentifier, StringComparison.Ordinal);
        packVersions.Add(appHostName, appHostVersion);

        var sdkRoot = Directory.GetParent(candidate.SdkDirectory)?.Parent?.FullName;
        if (string.IsNullOrWhiteSpace(sdkRoot)) return null;
        foreach (var pack in packVersions)
        {
            var packRoot = Path.Combine(sdkRoot, "packs", pack.Key, pack.Value);
            var complete = string.Equals(pack.Key, appHostName, StringComparison.Ordinal)
                ? File.Exists(Path.Combine(
                    packRoot, "runtimes", RuntimeIdentifier, "native", "apphost.exe"))
                : File.Exists(Path.Combine(packRoot, "data", "FrameworkList.xml")) &&
                  Directory.Exists(Path.Combine(packRoot, "ref", TargetFramework));
            if (!complete) return null;
        }
        return new(candidate.VersionText, packVersions);
    }

    private sealed record SdkCandidate(
        Version Version,
        string VersionText,
        string SdkDirectory);
}
