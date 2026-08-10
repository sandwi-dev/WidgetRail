namespace GameBarAlternative.WidgetRuntime;

internal interface IAppContainerAuthorityRecoveryService
{
    IReadOnlyList<AppContainerAuthorityRecoveryCandidate> ListPending();
    void Retry(string confirmationToken);
}

internal sealed class AppContainerAuthorityRecoveryService(
    FileAppContainerAuthorityJournal journal) : IAppContainerAuthorityRecoveryService
{
    private static readonly Lazy<AppContainerAuthorityRecoveryService> DefaultValue =
        new(() => new AppContainerAuthorityRecoveryService(
            FileAppContainerAuthorityJournal.Default),
            LazyThreadSafetyMode.ExecutionAndPublication);

    internal static AppContainerAuthorityRecoveryService Default => DefaultValue.Value;

    public IReadOnlyList<AppContainerAuthorityRecoveryCandidate> ListPending() =>
        journal.ListPending()
            .Select(transaction => new AppContainerAuthorityRecoveryCandidate(
                transaction.ConfirmationToken,
                transaction.ProfileName,
                transaction.Snapshots.Count,
                transaction.IsLegacy))
            .ToArray();

    public void Retry(string confirmationToken)
    {
        ValidateConfirmationToken(confirmationToken);
        var candidate = journal.ListPending().SingleOrDefault(transaction =>
            string.Equals(
                transaction.ConfirmationToken,
                confirmationToken,
                StringComparison.Ordinal));
        if (candidate is null)
            throw new AppContainerAuthorityRecoveryException("stale_confirmation");

        using var lease = journal.Acquire(candidate.ProfileName);
        var current = lease.ReadPending();
        if (current is null ||
            !string.Equals(
                current.ConfirmationToken,
                confirmationToken,
                StringComparison.Ordinal))
            throw new AppContainerAuthorityRecoveryException("stale_confirmation");
        try
        {
            using var container = WindowsAppContainer.OpenExistingProfile(current.ProfileName);
            using var operations = container.CreateAuthorityOperationsForTesting();
            AppContainerAuthorityTransaction.Recover(current.Snapshots, operations);
            lease.ClearPending();
        }
        catch (AppContainerAuthorityRecoveryException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new AppContainerAuthorityRecoveryException(
                "recovery_not_verified", exception);
        }
    }

    private static void ValidateConfirmationToken(string value)
    {
        if (value is null || value.Length is not (32 or 64) ||
            !value.All(character =>
                character is >= '0' and <= '9' or >= 'A' and <= 'F'))
            throw new AppContainerAuthorityRecoveryException("invalid_confirmation");
    }
}

internal sealed class AppContainerAuthorityRecoveryException(
    string code,
    Exception? innerException = null)
    : Exception("AppContainer authority recovery failed.", innerException)
{
    internal string Code { get; } = code;
}
