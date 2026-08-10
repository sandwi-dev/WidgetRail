namespace GameBarAlternative.WidgetRuntime;

internal interface IAppContainerAuthorityRecoveryService
{
    IReadOnlyList<AppContainerAuthorityRecoveryCandidate> ListPending(
        CancellationToken cancellationToken = default);
    void Retry(
        string confirmationToken,
        CancellationToken cancellationToken = default,
        AppContainerAuthorityRecoveryCommitGate? commitGate = null);
}

/// <summary>
/// Owns the single commit-versus-cancellation decision for one retry. Once
/// commit wins, a caller timeout must report the verified recovery rather than
/// cancellation; when cancellation wins, the journal record is retained.
/// </summary>
internal sealed class AppContainerAuthorityRecoveryCommitGate(
    CancellationToken cancellationToken,
    Action? beforeCommit = null)
{
    private readonly object _gate = new();
    private bool _commitWon;

    internal bool CommitWon
    {
        get { lock (_gate) return _commitWon; }
    }

    internal void Commit(Action clearPending)
    {
        ArgumentNullException.ThrowIfNull(clearPending);
        lock (_gate)
        {
            beforeCommit?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            clearPending();
            _commitWon = true;
        }
    }
}

internal sealed class AppContainerAuthorityRecoveryService(
    FileAppContainerAuthorityJournal journal) : IAppContainerAuthorityRecoveryService
{
    private static readonly Lazy<AppContainerAuthorityRecoveryService> DefaultValue =
        new(() => new AppContainerAuthorityRecoveryService(
            FileAppContainerAuthorityJournal.Default),
            LazyThreadSafetyMode.ExecutionAndPublication);

    internal static AppContainerAuthorityRecoveryService Default => DefaultValue.Value;

    public IReadOnlyList<AppContainerAuthorityRecoveryCandidate> ListPending(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = journal.ListPending();
        cancellationToken.ThrowIfCancellationRequested();
        return pending
            .Select(transaction => new AppContainerAuthorityRecoveryCandidate(
                transaction.ConfirmationToken,
                transaction.ProfileName,
                transaction.Snapshots.Count,
                transaction.IsLegacy))
            .ToArray();
    }

    public void Retry(
        string confirmationToken,
        CancellationToken cancellationToken = default,
        AppContainerAuthorityRecoveryCommitGate? commitGate = null)
    {
        ValidateConfirmationToken(confirmationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = journal.ListPending().SingleOrDefault(transaction =>
            string.Equals(
                transaction.ConfirmationToken,
                confirmationToken,
                StringComparison.Ordinal));
        cancellationToken.ThrowIfCancellationRequested();
        if (candidate is null)
            throw new AppContainerAuthorityRecoveryException("stale_confirmation");

        using var lease = journal.Acquire(candidate.ProfileName);
        cancellationToken.ThrowIfCancellationRequested();
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
            AppContainerAuthorityTransaction.Recover(
                current.Snapshots, operations, cancellationToken);
            (commitGate ?? new AppContainerAuthorityRecoveryCommitGate(cancellationToken))
                .Commit(lease.ClearPending);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
