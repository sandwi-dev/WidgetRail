namespace GameBarAlternative.AvaloniaPrototype;

internal sealed record PrototypeArguments(
    string? EvidencePath,
    string? SourceCommit,
    int ExitAfterSeconds,
    string? InstallationPath,
    bool ReducedMotion)
{
    private static PrototypeArguments current = new(null, null, 0, null, false);

    public static PrototypeArguments Current => current;

    public static void Initialize(IReadOnlyList<string> args)
    {
        string? evidencePath = null;
        string? sourceCommit = null;
        var exitAfterSeconds = 0;
        string? installationPath = null;
        var reducedMotion = false;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--evidence" when index + 1 < args.Count:
                    evidencePath = args[++index];
                    break;
                case "--source-commit" when index + 1 < args.Count:
                    sourceCommit = args[++index];
                    break;
                case "--exit-after-seconds" when index + 1 < args.Count:
                    if (!int.TryParse(args[++index], out exitAfterSeconds) || exitAfterSeconds is < 1 or > 60)
                    {
                        throw new ArgumentOutOfRangeException(nameof(args), "Exit duration must be between 1 and 60 seconds.");
                    }

                    break;
                case "--installation" when index + 1 < args.Count:
                    installationPath = Path.GetFullPath(args[++index]);
                    break;
                case "--reduced-motion":
                    reducedMotion = true;
                    break;
            }
        }

        current = new(evidencePath, sourceCommit, exitAfterSeconds, installationPath, reducedMotion);
    }
}
