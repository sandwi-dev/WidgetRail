using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.GbarCli;

internal sealed record LocalWidgetSdkBundle(
    string PackageId,
    string Version,
    string FileName,
    byte[] Content);

internal static class LocalWidgetSdkPackage
{
    private const int MaximumAssemblyBytes = 16 * 1024 * 1024;
    private static readonly DateTimeOffset ReproducibleTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static LocalWidgetSdkBundle Create()
    {
        var sdk = ReadAssembly(typeof(Widget).Assembly.Location, "WidgetSdk.dll");
        var protocol = ReadAssembly(
            typeof(ViewSnapshot).Assembly.Location, "WidgetProtocol.dll");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(sdk);
        hash.AppendData(protocol);
        var suffix = Convert.ToHexString(hash.GetHashAndReset())[..16]
            .ToLowerInvariant();
        var contract = WidgetSdkReleaseContract.Current;
        var version = contract.LocalPackageVersion(suffix);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(archive, $"{contract.PackageId}.nuspec", Nuspec(contract.PackageId, version));
            WriteText(archive, "[Content_Types].xml", ContentTypes);
            WriteText(archive, "_rels/.rels", Relationships(contract.PackageId));
            WriteBytes(archive, "lib/net8.0/WidgetSdk.dll", sdk);
            WriteBytes(archive, "lib/net8.0/WidgetProtocol.dll", protocol);
        }
        return new(
            contract.PackageId,
            version,
            $"{contract.PackageId}.{version}.nupkg",
            output.ToArray());
    }

    private static byte[] ReadAssembly(string path, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !string.Equals(Path.GetFileName(path), expectedName,
                StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException(
                $"The bundled {expectedName} required for offline scaffolding is unavailable. " +
                "Rebuild or reinstall gbar; no scaffold files were created.");
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumAssemblyBytes)
            throw new CliUsageException(
                $"The bundled {expectedName} is outside the supported SDK size bound. " +
                "Rebuild or reinstall gbar; no scaffold files were created.");
        var bytes = File.ReadAllBytes(path);
        StripCodeViewPath(bytes);
        return bytes;
    }

    private static void StripCodeViewPath(byte[] assembly)
    {
        using var stream = new MemoryStream(assembly, writable: false);
        using var reader = new PEReader(stream);
        foreach (var entry in reader.ReadDebugDirectory()
                     .Where(item => item.Type == DebugDirectoryEntryType.CodeView))
        {
            // RSDS consists of a 24-byte signature/GUID/age prefix followed by
            // a null-terminated absolute PDB path. The local compile package
            // needs reference metadata, not a machine-specific debugger path.
            const int codeViewPrefixBytes = 24;
            if (entry.DataSize <= codeViewPrefixBytes ||
                entry.DataPointer < 0 ||
                entry.DataPointer > assembly.Length - entry.DataSize)
                throw new CliUsageException(
                    "The bundled widget SDK has an invalid debug record. " +
                    "Rebuild or reinstall gbar; no scaffold files were created.");
            Array.Clear(
                assembly,
                entry.DataPointer + codeViewPrefixBytes,
                entry.DataSize - codeViewPrefixBytes);
        }
    }

    private static void WriteText(ZipArchive archive, string path, string content) =>
        WriteBytes(archive, path, new UTF8Encoding(false).GetBytes(content));

    private static void WriteBytes(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = ReproducibleTimestamp;
        entry.ExternalAttributes = unchecked((int)(0x81A4u << 16));
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string Nuspec(string packageId, string version) => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>{{packageId}}</id>
            <version>{{version}}</version>
            <authors>GameBarAlternative</authors>
            <requireLicenseAcceptance>false</requireLicenseAcceptance>
            <description>Local offline Game Bar Alternative widget SDK bundled by gbar.</description>
            <packageTypes>
              <packageType name="Dependency" />
            </packageTypes>
          </metadata>
        </package>
        """;

    private const string ContentTypes = """
        <?xml version="1.0" encoding="utf-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
          <Default Extension="nuspec" ContentType="application/octet" />
          <Default Extension="dll" ContentType="application/octet" />
        </Types>
        """;

    private static string Relationships(string packageId) => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest"
                        Target="/{{packageId}}.nuspec"
                        Id="R1" />
        </Relationships>
        """;
}
