using System.Text.Json;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace WinUiDeploymentProbe;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var result = new ProbeResult();
        FileStream? output = null;
        try
        {
            var options = Options.Parse(args);
            // Reserve evidence before any mutation. Never overwrite earlier results.
            output = new FileStream(options.Output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            result.Operation = options.Operation;
            result.RequestedStage = Stage.Load(options.Stage);
            var desired = result.RequestedStage;
            var expected = options.ExpectedCurrentStage is { } receipt ? Stage.Load(receipt) : null;
            if (expected is not null && (desired.PackageName != expected.PackageName || desired.Publisher != expected.Publisher))
                throw new InvalidDataException("Update/rollback must retain the exact probe identity and publisher.");
            if (options.AllowDowngrade && (options.Operation != "register" || expected is null || desired.Version >= expected.Version))
                throw new InvalidDataException("Explicit downgrade requires a lower target version and its exact current stage.");
            result.AllowDowngrade = options.AllowDowngrade;
            if (options.Operation == "validate")
            {
                result.Succeeded = true;
                return 0;
            }

            var manager = new PackageManager();
            result.Before = Find(manager, desired);
            if (options.Operation == "inspect") { result.After = result.Before; result.Succeeded = true; return 0; }
            if (options.Operation == "remove")
            {
                RequireOwned(result.Before, desired);
                if (options.ExpectedFullName != result.Before[0].FullName)
                    throw new InvalidOperationException("Removal requires the exact current full package name from prior readback.");
                desired.Revalidate();
                result.Api = "Windows.Management.Deployment.PackageManager.RemovePackageAsync";
                result.OperationStarted = true;
                try
                {
                    var removal = await manager.RemovePackageAsync(options.ExpectedFullName, RemovalOptions.PreserveApplicationData);
                    result.ActivityId = removal.ActivityId;
                    result.DeploymentErrorText = removal.ErrorText;
                    if (removal.ExtendedErrorCode is { HResult: < 0 } failure) throw failure;
                }
                finally { result.After = Find(manager, desired); }
                if (result.After.Length != 0) throw new InvalidOperationException("The exact probe identity remains registered after removal.");
                result.Succeeded = true;
                return 0;
            }
            if (expected is null)
            {
                if (result.Before.Length != 0) throw new InvalidOperationException("Initial registration refuses any existing matching identity; supply --expected-current-stage for an owned update.");
            }
            else RequireOwned(result.Before, expected);
            desired.Revalidate();
            expected?.Revalidate();
            var registerOptions = new RegisterPackageOptions
            {
                DeveloperMode = true,
                ExternalLocationUri = new Uri(desired.ExternalLocation + Path.DirectorySeparatorChar),
                ForceUpdateFromAnyVersion = options.AllowDowngrade,
                ForceAppShutdown = false,
                ForceTargetAppShutdown = false,
                DeferRegistrationWhenPackagesAreInUse = false,
            };
            result.Api = "Windows.Management.Deployment.PackageManager.RegisterPackageByUriAsync";
            result.OperationStarted = true;
            try
            {
                var deployment = await manager.RegisterPackageByUriAsync(new Uri(desired.Manifest), registerOptions);
                result.ActivityId = deployment.ActivityId;
                result.ExtendedError = deployment.ExtendedErrorCode is { } error ? $"0x{error.HResult:X8}" : null;
                result.DeploymentErrorText = deployment.ErrorText;
                if (deployment.ExtendedErrorCode is { HResult: < 0 } failure) throw failure;
            }
            finally { result.After = Find(manager, desired); }
            RequireOwned(result.After, desired);
            result.Succeeded = true;
            return 0;
        }
        catch (Exception error)
        {
            result.Error = error.ToString();
            result.HResult = $"0x{error.HResult:X8}";
            return 1;
        }
        finally
        {
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            if (output is not null)
            {
                await using (output)
                {
                    await using var writer = new StreamWriter(output, leaveOpen: true);
                    await writer.WriteAsync(json);
                }
            }
            Console.WriteLine(json);
        }
    }

    private static ObservedPackage[] Find(PackageManager manager, Stage stage) =>
        manager.FindPackagesForUser(string.Empty, stage.PackageName, stage.Publisher)
            .Select(package => new ObservedPackage(package.Id.Name, package.Id.Publisher, package.Id.FullName,
                package.Id.FamilyName, FormatVersion(package.Id.Version), package.IsDevelopmentMode,
                package.EffectiveExternalLocation?.Path, package.InstalledLocation.Path)).ToArray();

    private static string FormatVersion(PackageVersion version) => $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    private static void RequireOwned(ObservedPackage[] packages, Stage stage)
    {
        if (packages.Length != 1 || packages[0] is not { IsDevelopmentMode: true, ExternalLocation: { } external } package ||
            package.Name != stage.PackageName || package.Publisher != stage.Publisher || package.Version != stage.Version.ToString() ||
            !Stage.SamePath(external, stage.ExternalLocation))
            throw new InvalidOperationException("Registration does not exactly match the supplied owned development stage.");
    }

    private sealed record ObservedPackage(string Name, string Publisher, string FullName, string FamilyName,
        string Version, bool IsDevelopmentMode, string? ExternalLocation, string InstalledLocation);
    private sealed class ProbeResult
    {
        public string Mode { get; } = "unsigned-development-manifest";
        public bool QualifiesSignedProductionDeployment { get; } = false;
        public bool InstallsRuntimePrerequisites { get; } = false;
        public bool ChangesCertificateTrust { get; } = false;
        public bool LaunchesApplication { get; } = false;
        public string? Operation { get; set; }
        public string? Api { get; set; }
        public bool OperationStarted { get; set; }
        public bool AllowDowngrade { get; set; }
        public bool Succeeded { get; set; }
        public string Status => Succeeded ? "completed" : OperationStarted ? "failed" : "rejected";
        public Stage? RequestedStage { get; set; }
        public ObservedPackage[] Before { get; set; } = [];
        public ObservedPackage[] After { get; set; } = [];
        public Guid? ActivityId { get; set; }
        public string? ExtendedError { get; set; }
        public string? DeploymentErrorText { get; set; }
        public string? Error { get; set; }
        public string? HResult { get; set; }
    }

    private sealed record Options(string Operation, string Stage, string Output, string? ExpectedCurrentStage, bool AllowDowngrade, string? ExpectedFullName)
    {
        internal static Options Parse(string[] args)
        {
            if (args.Length == 0 || args[0] is not ("validate" or "inspect" or "register" or "remove"))
                throw new ArgumentException("Use validate|inspect|register|remove --stage <absolute stage.json> --output <new absolute result.json> [--expected-current-stage <stage.json>] [--allow-downgrade] [--expected-full-name <owned name for remove>].");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            bool downgrade = false;
            for (var index = 1; index < args.Length; index++)
            {
                var key = args[index];
                if (key == "--allow-downgrade")
                {
                    if (downgrade) throw new ArgumentException("Duplicate downgrade flag.");
                    downgrade = true; continue;
                }
                if (key is not ("--stage" or "--output" or "--expected-current-stage" or "--expected-full-name") || index + 1 == args.Length ||
                    !values.TryAdd(key, args[++index])) throw new ArgumentException("Unknown, incomplete or duplicate option.");
            }
            if (!values.TryGetValue("--stage", out var stage) || !values.TryGetValue("--output", out var output) || !Path.IsPathFullyQualified(output))
                throw new ArgumentException("Absolute stage and new output paths are required.");
            output = Path.GetFullPath(output);
            StagePathCheck(output);
            var expectedName = values.GetValueOrDefault("--expected-full-name");
            if ((args[0] == "remove") != (expectedName is not null) ||
                args[0] != "register" && (downgrade || values.ContainsKey("--expected-current-stage")))
                throw new ArgumentException("Removal needs an exact full name; registration-only flags cannot be used on other operations.");
            return new(args[0], stage, output, values.GetValueOrDefault("--expected-current-stage"), downgrade, expectedName);
        }
        private static void StagePathCheck(string output)
        {
            WinUiDeploymentProbe.Stage.ExistingPath(Path.GetDirectoryName(output)!, directory: true);
            if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Evidence output already exists.");
        }
    }
}
