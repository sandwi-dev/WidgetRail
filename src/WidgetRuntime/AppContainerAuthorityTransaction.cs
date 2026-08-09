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

internal readonly record struct AppContainerAuthoritySnapshot(
    AppContainerAuthorityTarget Target,
    string AccessDescriptor);

internal interface IAppContainerAuthorityOperations
{
    AppContainerAuthoritySnapshot Capture(AppContainerAuthorityTarget target);
    void Apply(AppContainerAuthoritySnapshot snapshot);
    void Restore(AppContainerAuthoritySnapshot snapshot);
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
    internal static void Apply(
        IReadOnlyList<AppContainerAuthorityTarget> targets,
        IAppContainerAuthorityOperations operations)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(operations);

        var attempted = new List<AppContainerAuthoritySnapshot>(targets.Count);
        try
        {
            foreach (var target in targets)
            {
                var snapshot = operations.Capture(target);
                attempted.Add(snapshot);
                operations.Apply(snapshot);
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
}
