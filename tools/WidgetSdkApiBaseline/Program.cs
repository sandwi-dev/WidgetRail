if (args is not ["update"])
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project tools/WidgetSdkApiBaseline/WidgetSdkApiBaseline.csproj " +
        "--configuration Release -- update");
    return 2;
}

var root = FindRepositoryRoot();
var baseline = Path.Combine(root, "src", "WidgetSdk", "PublicApi.txt");
var count = await WidgetSdkBaselineUpdater.UpdateAsync(baseline);
Console.WriteLine(
    $"Updated {Path.GetRelativePath(root, baseline)} with {count} public API symbols.");
return 0;

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(Environment.CurrentDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(
                current.FullName, "src", "WidgetSdk", "WidgetSdk.csproj")) &&
            File.Exists(Path.Combine(
                current.FullName, "src", "WidgetSdk", "PublicApi.txt")))
            return current.FullName;
        current = current.Parent;
    }
    throw new InvalidOperationException(
        "Could not locate src/WidgetSdk/PublicApi.txt from this checkout.");
}
