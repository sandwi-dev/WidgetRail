using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using WidgetRail.WrailCli;
using WidgetRail.WidgetSdk;

internal static class WidgetSdkReleaseUnitScenarios
{
    public static Task Run()
    {
        var contract = WidgetSdkReleaseContract.Current;
        Equal("0.3.0-dev", contract.Version);
        Equal("WidgetRail.WidgetSdk", contract.PackageId);
        Equal(ControllerWidgetScaffolder.SupportedTemplateVersion, contract.TemplateVersion);

        var first = LocalWidgetSdkPackage.Create();
        var second = LocalWidgetSdkPackage.Create();
        Equal(contract.PackageId, first.PackageId);
        True(first.Version.StartsWith(contract.Version + ".local.", StringComparison.Ordinal),
            "Local package version did not retain the canonical pre-release base.");
        Equal(contract.Version.Length + ".local.".Length + 16, first.Version.Length);
        Equal($"{contract.PackageId}.{first.Version}.nupkg", first.FileName);
        True(first.Content.AsSpan().SequenceEqual(second.Content),
            "Repeated local SDK packaging was not byte deterministic.");

        using var archive = new ZipArchive(
            new MemoryStream(first.Content, writable: false), ZipArchiveMode.Read);
        var nuspecEntry = archive.GetEntry(contract.PackageId + ".nuspec")
            ?? throw new InvalidOperationException("Local SDK package omitted its nuspec.");
        using var reader = new StreamReader(nuspecEntry.Open(), Encoding.UTF8);
        var nuspec = reader.ReadToEnd();
        Contains($"<id>{contract.PackageId}</id>", nuspec);
        Contains($"<version>{first.Version}</version>", nuspec);
        Contains("<license type=\"expression\">MIT</license>", nuspec);
        Contains("<files include=\"any/any/WidgetRail/EmbeddedMediaAdapterRuntime.js\"", nuspec);
        Contains("buildAction=\"None\"", nuspec);
        Contains("copyToOutput=\"false\"", nuspec);
        Contains("flatten=\"false\"", nuspec);
        True(!nuspec.Contains(Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase),
            "Local SDK package metadata leaked the checkout path.");

        var adapterRuntime = ReadEntry(archive, LocalWidgetSdkPackage.AdapterRuntimePackagePath);
        True(adapterRuntime.Length > 0,
            "Local SDK package emitted an empty embedded-media adapter runtime.");
        Contains("WidgetRailEmbeddedMediaAdapter", Encoding.UTF8.GetString(adapterRuntime));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in new[]
                 {
                     "lib/net8.0/WidgetSdk.dll",
                     "lib/net8.0/WidgetProtocol.dll",
                     "lib/net8.0/WidgetApplicationRuntime.dll",
                 })
            hash.AppendData(ReadEntry(archive, path));
        hash.AppendData(adapterRuntime);
        hash.AppendData(Encoding.UTF8.GetBytes(
            LocalWidgetSdkPackage.AdapterRuntimeContentFilesContract));
        hash.AppendData(Encoding.UTF8.GetBytes(LocalWidgetSdkPackage.LicenseExpression));
        var expectedSuffix = Convert.ToHexString(hash.GetHashAndReset())[..16]
            .ToLowerInvariant();
        Equal(contract.LocalPackageVersion(expectedSuffix), first.Version);

        foreach (var assembly in new[] { typeof(Widget).Assembly, typeof(CliApplication).Assembly })
        {
            var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToDictionary(item => item.Key, item => item.Value ?? string.Empty, StringComparer.Ordinal);
            Equal(contract.Version, metadata["WidgetSdkReleaseVersion"]);
            Equal(contract.PackageId, metadata["WidgetSdkPackageId"]);
            Equal(contract.TemplateVersion.ToString(), metadata["ControllerWidgetTemplateVersion"]);
        }
        return Task.CompletedTask;
    }

    private static byte[] ReadEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)
            ?? throw new InvalidOperationException($"Local SDK package omitted '{path}'.");
        using var stream = entry.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected '{expected}' in package metadata.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
