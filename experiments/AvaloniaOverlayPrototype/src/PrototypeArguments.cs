namespace GameBarAlternative.AvaloniaPrototype;

internal sealed record PrototypeArguments(
    string? EvidencePath,
    string? SourceCommit,
    string? FocusedVerificationCommit,
    int ExitAfterSeconds,
    string? InstallationPath,
    bool ReducedMotion,
    string? InputTracePath)
{
    private static PrototypeArguments current = new(null, null, null, 0, null, false, null);

    public static PrototypeArguments Current => current;

    public static void Initialize(IReadOnlyList<string> args)
    {
        string? evidencePath = null;
        string? sourceCommit = null;
        string? focusedVerificationCommit = null;
        var exitAfterSeconds = 0;
        string? installationPath = null;
        var reducedMotion = false;
        string? inputTracePath = null;

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
                case "--focused-verification-commit" when index + 1 < args.Count:
                    focusedVerificationCommit = args[++index];
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
                case "--input-trace" when index + 1 < args.Count:
                    inputTracePath = Path.GetFullPath(args[++index]);
                    break;
            }
        }

        current = new(
            evidencePath,
            sourceCommit,
            focusedVerificationCommit,
            exitAfterSeconds,
            installationPath,
            reducedMotion,
            inputTracePath);
    }
}
