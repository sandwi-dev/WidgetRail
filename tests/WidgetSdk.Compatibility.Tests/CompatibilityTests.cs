using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using WidgetRail.WrailCli;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WidgetRail.WidgetSdk.Compatibility.Tests;

[TestClass]
public sealed class CompatibilityTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string BaselinePath = Path.Combine(
        RepositoryRoot, "src", "WidgetSdk", "PublicApi.txt");
    private static readonly string InitialBaselineHash = Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(BaselinePath)));

    [TestMethod]
    public void CurrentPublicApiMatchesReviewedBaseline()
    {
        var current = WidgetSdkBaselineContract.GenerateCurrent();
        var expected = WidgetSdkBaselineContract.ReadBaseline(
            BaselinePath, RepositoryRoot);
        var diff = ApiDiff.Compare(expected, current);
        AssertNoDiff(diff);
    }

    [TestMethod]
    public void PublicApiGenerationIsDeterministicAndBounded()
    {
        var first = WidgetSdkBaselineContract.GenerateCurrent();
        var second = WidgetSdkBaselineContract.GenerateCurrent();
        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
        Assert.IsGreaterThan(0, first.Count);
        Assert.IsLessThanOrEqualTo(
            WidgetSdkBaselineContract.MaximumSymbols, first.Count);
        Assert.IsLessThanOrEqualTo(
            WidgetSdkBaselineContract.MaximumBaselineBytes,
            WidgetSdkPublicApi.Serialize(first).Length);
    }

    [TestMethod]
    public void ShortcutOverloadsRetainLegacyClrMembersAndAddLabels()
    {
        var containerLegacy = new[]
        {
            typeof(ControllerButton),
            typeof(string),
            typeof(ControllerEventPhase),
            typeof(ControllerActionRepeatPolicy),
        };
        var containerLabeled = new[]
        {
            typeof(ControllerButton),
            typeof(string),
            typeof(string),
            typeof(ControllerEventPhase),
            typeof(ControllerActionRepeatPolicy),
        };
        foreach (var (type, returnType) in new[]
                 {
                     (typeof(ContainerElement), typeof(ContainerElement)),
                     (typeof(StackElement), typeof(StackElement)),
                     (typeof(RowElement), typeof(RowElement)),
                     (typeof(ScrollElement), typeof(ScrollElement)),
                     (typeof(GridElement), typeof(GridElement)),
                 })
        {
            AssertShortcutOverload(type, returnType, containerLegacy, hasLabel: false);
            AssertShortcutOverload(type, returnType, containerLabeled, hasLabel: true);
        }

        var focusableLegacy = new[]
        {
            typeof(ControllerButton),
            typeof(ControllerEventPhase),
            typeof(string),
            typeof(ControllerActionRepeatPolicy),
        };
        var focusableLabeled = new[]
        {
            typeof(ControllerButton),
            typeof(string),
            typeof(ControllerEventPhase),
            typeof(string),
            typeof(ControllerActionRepeatPolicy),
        };
        foreach (var type in new[] { typeof(ButtonElement), typeof(ActionSurfaceElement) })
        {
            AssertShortcutOverload(type, type, focusableLegacy, hasLabel: false);
            AssertShortcutOverload(type, type, focusableLabeled, hasLabel: true);
        }
    }

    private static void AssertShortcutOverload(
        Type declaringType,
        Type returnType,
        Type[] parameterTypes,
        bool hasLabel)
    {
        var method = declaringType.GetMethod(
            "Shortcut",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        Assert.IsNotNull(method,
            $"{declaringType.Name} is missing its {(hasLabel ? "labeled" : "legacy")} Shortcut CLR member.");
        Assert.AreEqual(returnType, method.ReturnType);
        var labels = method.GetParameters().Where(parameter => parameter.Name == "label").ToArray();
        Assert.AreEqual(hasLabel ? 1 : 0, labels.Length);
        if (hasLabel) Assert.IsFalse(labels[0].IsOptional);
    }

    [TestMethod]
    public void ActionDiagnosticHookRemainsProtectedVirtualAndNonPublic()
    {
        var parameterTypes = new[]
        {
            typeof(WidgetActionEvent),
            typeof(string),
            typeof(string),
        };
        var method = typeof(Widget).GetMethod(
            "OnActionDiagnostic",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        Assert.IsNotNull(method);
        Assert.IsTrue(method.IsFamily);
        Assert.IsFalse(method.IsPublic);
        Assert.IsFalse(method.IsStatic);
        Assert.IsTrue(method.IsVirtual);
        Assert.IsFalse(method.IsFinal);
        Assert.AreEqual(typeof(void), method.ReturnType);
        CollectionAssert.AreEqual(
            parameterTypes,
            method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.AreSame(method, method.GetBaseDefinition());
        Assert.IsNull(typeof(Widget).GetMethod(
            "OnActionDiagnostic",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: parameterTypes,
            modifiers: null));
    }

    [TestMethod]
    public void DiffClassifiesCompatibleAdditionExactly()
    {
        var diff = ApiDiff.Compare(
            ["type A"],
            ["type A", "method A.M() -> void"]);
        CollectionAssert.AreEqual(
            Array.Empty<string>(), diff.Removed.ToArray());
        CollectionAssert.AreEqual(
            new[] { "method A.M() -> void" }, diff.Added.ToArray());
    }

    [TestMethod]
    public void DiffClassifiesRemovalExactly()
    {
        var diff = ApiDiff.Compare(
            ["type A", "method A.M() -> void"],
            ["type A"]);
        CollectionAssert.AreEqual(
            new[] { "method A.M() -> void" }, diff.Removed.ToArray());
        CollectionAssert.AreEqual(
            Array.Empty<string>(), diff.Added.ToArray());
    }

    [TestMethod]
    public void DiffClassifiesBothSidesOfSignatureChangeExactly()
    {
        var diff = ApiDiff.Compare(
            ["method A.M(int value) -> void"],
            ["method A.M(string value) -> void"]);
        CollectionAssert.AreEqual(
            new[] { "method A.M(int value) -> void" }, diff.Removed.ToArray());
        CollectionAssert.AreEqual(
            new[] { "method A.M(string value) -> void" }, diff.Added.ToArray());
    }

    [TestMethod]
    public void ReleaseUnitMetadataTemplateAndDependencyMatch()
    {
        var propsPath = Path.Combine(
            RepositoryRoot, "eng", "WidgetSdkRelease.props");
        var document = XDocument.Load(propsPath, LoadOptions.None);
        var properties = document.Descendants("PropertyGroup").Elements()
            .ToDictionary(
                element => element.Name.LocalName,
                element => element.Value,
                StringComparer.Ordinal);
        var version = Required(properties, "WidgetSdkReleaseVersion");
        var packageId = Required(properties, "WidgetSdkPackageId");
        var templateVersion = Required(
            properties, "ControllerWidgetTemplateVersion");
        Assert.AreEqual("WidgetRail.WidgetSdk", packageId);
        Assert.IsTrue(
            int.TryParse(templateVersion, out var parsedTemplateVersion) &&
            parsedTemplateVersion > 0,
            $"Invalid ControllerWidget template version '{templateVersion}'.");

        foreach (var assembly in new[]
                 {
                     typeof(Widget).Assembly,
                     typeof(CliApplication).Assembly,
                 })
        {
            var metadata = assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToDictionary(
                    item => item.Key,
                    item => item.Value ?? string.Empty,
                    StringComparer.Ordinal);
            Assert.AreEqual(version,
                metadata.GetValueOrDefault("WidgetSdkReleaseVersion"));
            Assert.AreEqual(packageId,
                metadata.GetValueOrDefault("WidgetSdkPackageId"));
            Assert.AreEqual(templateVersion,
                metadata.GetValueOrDefault("ControllerWidgetTemplateVersion"));
            Assert.AreEqual(version, ReleaseVersion(assembly));
        }

        using var manifestDocument = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(
                RepositoryRoot,
                "templates", "ControllerWidget", "template.json")));
        Assert.AreEqual(
            parsedTemplateVersion,
            manifestDocument.RootElement
                .GetProperty("templateVersion").GetInt32());
        var projectTemplate = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "templates", "ControllerWidget", "WidgetName.csproj.template"));
        StringAssert.Contains(
            projectTemplate,
            "PackageReference Include=\"{{SdkPackageId}}\" Version=\"{{SdkVersion}}\"");
    }

    [TestMethod]
    public void MissingBaselineIsRejected()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "missing.txt");
        var exception = Assert.ThrowsExactly<FileNotFoundException>(() =>
            WidgetSdkBaselineContract.ReadBaseline(path, temporary.Path));
        StringAssert.Contains(exception.Message, "baseline is missing");
    }

    [TestMethod]
    public void OversizedBaselineIsRejected()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "oversized.txt");
        File.WriteAllBytes(
            path,
            new byte[WidgetSdkBaselineContract.MaximumBaselineBytes + 1]);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            WidgetSdkBaselineContract.ReadBaseline(path, temporary.Path));
        StringAssert.Contains(exception.Message, "1-MiB bound");
    }

    [TestMethod]
    public void MalformedDuplicateAndUnsortedBaselinesAreRejected()
    {
        AssertMalformed("not a baseline\n", "unsupported header");
        AssertMalformed(
            "# WidgetSdk public API baseline v1\ntype A\ntype A\n",
            "duplicate symbols");
        AssertMalformed(
            "# WidgetSdk public API baseline v1\ntype B\ntype A\n",
            "not ordinally sorted");
    }

    [TestMethod]
    public void CheckoutPathInBaselineIsRejected()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "PublicApi.txt");
        File.WriteAllText(
            path,
            "# WidgetSdk public API baseline v1\n" +
            $"field string A.Path = \"{temporary.Path.Replace("\\", "\\\\", StringComparison.Ordinal)}\"\n");
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            WidgetSdkBaselineContract.ReadBaseline(path, temporary.Path));
        StringAssert.Contains(exception.Message, "absolute checkout path");
    }

    [TestMethod]
    public async Task BaselineUpdaterWritesOnlyRequestedBoundedFile()
    {
        using var temporary = new TemporaryDirectory();
        var sentinel = Path.Combine(temporary.Path, "sentinel.txt");
        var baseline = Path.Combine(temporary.Path, "PublicApi.txt");
        await File.WriteAllTextAsync(sentinel, "unchanged");

        var count = await WidgetSdkBaselineUpdater.UpdateAsync(baseline);

        Assert.AreEqual(
            WidgetSdkBaselineContract.GenerateCurrent().Count, count);
        Assert.AreEqual("unchanged", await File.ReadAllTextAsync(sentinel));
        Assert.IsTrue(File.Exists(baseline));
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(temporary.Path)
            .Any(path => Path.GetFileName(path).Contains(".tmp-", StringComparison.Ordinal)));
        Assert.IsLessThanOrEqualTo(
            WidgetSdkBaselineContract.MaximumBaselineBytes,
            new FileInfo(baseline).Length);
    }

    [TestMethod]
    public void OrdinaryTestExecutionDoesNotMutateReviewedBaseline()
    {
        Assert.AreEqual(
            InitialBaselineHash,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(BaselinePath))));
    }

    private static void AssertMalformed(string text, string expected)
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "PublicApi.txt");
        File.WriteAllText(path, text);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            WidgetSdkBaselineContract.ReadBaseline(path, temporary.Path));
        StringAssert.Contains(exception.Message, expected);
    }

    private static void AssertNoDiff(ApiDiff diff)
    {
        if (diff.Removed.Count == 0 && diff.Added.Count == 0) return;
        Assert.Fail(
            string.Join(Environment.NewLine,
                diff.Removed.Select(symbol => $"REMOVED_OR_CHANGED - {symbol}")
                    .Concat(diff.Added.Select(symbol =>
                        $"COMPATIBLE_ADDITION_OR_CHANGED + {symbol}"))) +
            Environment.NewLine +
            "Review compatibility, then intentionally update only the baseline with: " +
            "dotnet run --project tools/WidgetSdkApiBaseline/WidgetSdkApiBaseline.csproj " +
            "--configuration Release -- update");
    }

    private static string Required(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        Assert.IsTrue(values.TryGetValue(key, out var value));
        Assert.IsFalse(string.IsNullOrWhiteSpace(value));
        return value!;
    }

    private static string ReleaseVersion(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        Assert.IsNotNull(informational);
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName, "eng", "WidgetSdkRelease.props")) &&
                File.Exists(Path.Combine(
                    current.FullName, "src", "WidgetSdk", "WidgetSdk.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate the WidgetSdk release contract.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"widget-sdk-compat-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
