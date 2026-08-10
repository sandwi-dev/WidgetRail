using System.Runtime.ExceptionServices;

namespace GameBarAlternative.WidgetRuntime;

internal enum AppContainerAuthorityTargetKind
{
    AuthorityRoot,
    VerifiedDirectory,
    VerifiedFile,
}

internal readonly record struct AppContainerAuthorityTarget(
    string Path,
    AppContainerAuthorityTargetKind Kind);

internal readonly record struct AppContainerAuthorityObjectIdentity(
    ulong VolumeSerialNumber,
    string FileId);

internal readonly record struct AppContainerAuthoritySnapshot(
    AppContainerAuthorityTarget Target,
    string AccessDescriptor,
    AppContainerAuthorityObjectIdentity ObjectIdentity);

internal interface IAppContainerAuthorityOperations : IDisposable
{
    AppContainerAuthoritySnapshot Capture(AppContainerAuthorityTarget target);
    void Apply(AppContainerAuthoritySnapshot snapshot);
    void VerifyApplied(AppContainerAuthoritySnapshot snapshot);
    void Restore(AppContainerAuthoritySnapshot snapshot);
    void VerifyRestored(AppContainerAuthoritySnapshot snapshot);
}

internal sealed class AppContainerAuthorityRollbackException(
    Exception applyFailure,
    IReadOnlyList<Exception> rollbackFailures)
    : Exception("AppContainer content authority rollback failed.", applyFailure)
{
    public Exception ApplyFailure { get; } = applyFailure;
    public IReadOnlyList<Exception> RollbackFailures { get; } = rollbackFailures;
}

/// <summary>
/// Applies one bounded exact-content DACL transaction. Every attempted target
/// is restored in reverse order when admission fails, including the target
/// whose write reported failure because a filesystem write may have partially
/// committed before throwing.
/// </summary>
internal static class AppContainerAuthorityTransaction
{
    internal static IReadOnlyList<AppContainerAuthoritySnapshot> Capture(
        IReadOnlyList<AppContainerAuthorityTarget> targets,
        IAppContainerAuthorityOperations operations)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(operations);

        var snapshots = new List<AppContainerAuthoritySnapshot>(targets.Count);
        foreach (var target in targets) snapshots.Add(operations.Capture(target));
        return snapshots.AsReadOnly();
    }

    internal static void Apply(
        IReadOnlyList<AppContainerAuthorityTarget> targets,
        IAppContainerAuthorityOperations operations) =>
        Apply(Capture(targets, operations), operations);

    internal static void Apply(
        IReadOnlyList<AppContainerAuthoritySnapshot> snapshots,
        IAppContainerAuthorityOperations operations)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(operations);

        var attempted = new List<AppContainerAuthoritySnapshot>(snapshots.Count);
        try
        {
            foreach (var snapshot in snapshots)
            {
                attempted.Add(snapshot);
                operations.Apply(snapshot);
                operations.VerifyApplied(snapshot);
            }
        }
        catch (Exception applyFailure) when (applyFailure is not OutOfMemoryException)
        {
            List<Exception>? rollbackFailures = null;
            for (var index = attempted.Count - 1; index >= 0; index--)
            {
                try
                {
                    operations.Restore(attempted[index]);
                    operations.VerifyRestored(attempted[index]);
                }
                catch (Exception rollbackFailure) when (
                    rollbackFailure is not OutOfMemoryException)
                {
                    (rollbackFailures ??= []).Add(rollbackFailure);
                }
            }
            if (rollbackFailures is not null)
                throw new AppContainerAuthorityRollbackException(
                    applyFailure, rollbackFailures.AsReadOnly());
            ExceptionDispatchInfo.Capture(applyFailure).Throw();
            throw;
        }
    }

    internal static void Recover(
        IReadOnlyList<AppContainerAuthoritySnapshot> snapshots,
        IAppContainerAuthorityOperations operations)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(operations);

        List<Exception>? failures = null;
        for (var index = snapshots.Count - 1; index >= 0; index--)
        {
            try
            {
                operations.Restore(snapshots[index]);
                operations.VerifyRestored(snapshots[index]);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                (failures ??= []).Add(failure);
            }
        }
        if (failures is not null)
            throw new AggregateException(
                "Pending AppContainer content authority could not be recovered.",
                failures);
    }
}
