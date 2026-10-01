internal static class VerificationInstallation
{
    internal static string Configuration => Environment.GetEnvironmentVariable("WRAIL_TEST_CONFIGURATION") is { Length: > 0 } value ? value : "Release";
    internal static string Root
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("WRAIL_TEST_INSTALLATION_ROOT");
            if (string.IsNullOrEmpty(value) || !Path.IsPathFullyQualified(value) || !File.Exists(Path.Combine(value, "widget-catalog.json")))
                throw new InvalidOperationException("Build and test the coherent managed installation with scripts/Test-WidgetBridgeRuntime.ps1; no retired-renderer output is used.");
            return value;
        }
    }
}
