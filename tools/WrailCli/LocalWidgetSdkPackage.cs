using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetRuntime;

namespace WidgetRail.WrailCli;

internal sealed record LocalWidgetSdkBundle(
    string PackageId,
    string Version,
    string FileName,
    byte[] Content);

internal static class LocalWidgetSdkPackage
{
    internal const string LicenseExpression = "MPL-2.0";
    private const int MaximumAssemblyBytes = 16 * 1024 * 1024;
    private const int MaximumAdapterRuntimeBytes = 256 * 1024;
    internal const string AdapterRuntimePackagePath =
        "contentFiles/any/any/WidgetRail/EmbeddedMediaAdapterRuntime.js";
    internal const string AdapterRuntimeContentFilesContract =
        "any/any/WidgetRail/EmbeddedMediaAdapterRuntime.js|None|false|false";
    private const string AdapterRuntimeResourceName =
        "WidgetRail.WrailCli.EmbeddedMediaAdapterRuntime.js";
    private static readonly DateTimeOffset ReproducibleTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static LocalWidgetSdkBundle Create()
    {
        var sdk = ReadAssembly(typeof(Widget).Assembly.Location, "WidgetSdk.dll");
        var protocol = ReadAssembly(
            typeof(ViewSnapshot).Assembly.Location, "WidgetProtocol.dll");
        var applicationRuntime = ReadAssembly(
            typeof(WidgetApplicationBootstrap).Assembly.Location,
            "WidgetApplicationRuntime.dll");
        var adapterRuntime = ReadAdapterRuntime();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(sdk);
        hash.AppendData(protocol);
        hash.AppendData(applicationRuntime);
        hash.AppendData(adapterRuntime);
        hash.AppendData(Encoding.UTF8.GetBytes(AdapterRuntimeContentFilesContract));
        hash.AppendData(Encoding.UTF8.GetBytes(LicenseExpression));
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
            WriteBytes(archive, "lib/net8.0/WidgetApplicationRuntime.dll",
                applicationRuntime);
            WriteBytes(archive, AdapterRuntimePackagePath, adapterRuntime);
        }
        return new(
            contract.PackageId,
            version,
            $"{contract.PackageId}.{version}.nupkg",
            output.ToArray());
    }

    private static byte[] ReadAdapterRuntime()
    {
        using var stream = typeof(LocalWidgetSdkPackage).Assembly
            .GetManifestResourceStream(AdapterRuntimeResourceName)
            ?? throw new CliUsageException(
                "The bundled embedded-media adapter runtime required for offline scaffolding " +
                "is unavailable. Rebuild or reinstall wrail; no scaffold files were created.");
        if (stream.Length is <= 0 or > MaximumAdapterRuntimeBytes)
            throw new CliUsageException(
                "The bundled embedded-media adapter runtime is outside the supported size bound. " +
                "Rebuild or reinstall wrail; no scaffold files were created.");
        using var output = new MemoryStream((int)stream.Length);
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] ReadAssembly(string path, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !string.Equals(Path.GetFileName(path), expectedName,
                StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException(
                $"The bundled {expectedName} required for offline scaffolding is unavailable. " +
                "Rebuild or reinstall wrail; no scaffold files were created.");
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumAssemblyBytes)
            throw new CliUsageException(
                $"The bundled {expectedName} is outside the supported SDK size bound. " +
                "Rebuild or reinstall wrail; no scaffold files were created.");
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
                    "Rebuild or reinstall wrail; no scaffold files were created.");
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
            <authors>WidgetRail</authors>
            <requireLicenseAcceptance>false</requireLicenseAcceptance>
            <license type="expression">{{LicenseExpression}}</license>
            <description>Local offline WidgetRail widget SDK bundled by wrail.</description>
            <packageTypes>
              <packageType name="Dependency" />
            </packageTypes>
            <contentFiles>
              <files include="any/any/WidgetRail/EmbeddedMediaAdapterRuntime.js"
                     buildAction="None"
                     copyToOutput="false"
                     flatten="false" />
            </contentFiles>
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
