namespace GameBarAlternative.GbarCli;

using GameBarAlternative.WidgetRuntime;

internal sealed record AuthorityRecoverySummary(
    string ConfirmationToken,
    string ProfileName,
    int TargetCount,
    bool IsLegacy);

internal interface IAuthorityRecoveryClient
{
    IReadOnlyList<AuthorityRecoverySummary> ListPending(
        CancellationToken cancellationToken = default);
    void Retry(
        string confirmationToken,
        CancellationToken cancellationToken = default);
}

internal sealed class AuthorityRecoveryClientException(
    string code,
    Exception? innerException = null)
    : Exception("Authority recovery client failed.", innerException)
{
    internal string Code { get; } = code;
}

internal static class AuthorityRecoveryCommand
{
    internal static Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RunAsync(
            args, output, RuntimeAuthorityRecoveryClient.Instance, cancellationToken);
    }

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        IAuthorityRecoveryClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(client);
        cancellationToken.ThrowIfCancellationRequested();

        if (args is ["help"])
        {
            await output.WriteLineAsync(CliApplication.HelpText);
            return 0;
        }
        if (args is ["list"])
        {
            IReadOnlyList<AuthorityRecoverySummary> pending;
            try
            {
                pending = client.ListPending(cancellationToken);
            }
            catch (AuthorityRecoveryClientException exception)
            {
                throw Failure(exception);
            }

            if (pending.Count == 0)
            {
                await output.WriteLineAsync(
                    "No pending AppContainer authority recovery transactions.");
                return 0;
            }

            var ordered = pending
                .OrderBy(value => value.ProfileName, StringComparer.Ordinal)
                .ThenBy(value => value.ConfirmationToken, StringComparer.Ordinal)
                .ToArray();
            if (ordered.Any(candidate => !IsSafe(candidate)))
                throw new CliOperationException(
                    "Authority recovery failed (invalid_recovery_state).");
            foreach (var candidate in ordered)
            {
                var format = candidate.IsLegacy ? "legacy" : "current";
                await output.WriteLineAsync(
                    $"pending  {candidate.ConfirmationToken}  " +
                    $"profile={candidate.ProfileName}  " +
                    $"targets={candidate.TargetCount}  format={format}");
            }
            await output.WriteLineAsync(
                "Retry one exact transaction with: " +
                "gbar authority-recovery retry <confirmation-token>");
            return 0;
        }
        if (args is ["retry", var confirmationToken])
        {
            if (!IsConfirmationToken(confirmationToken))
                throw new CliUsageException(
                    "authority-recovery retry requires the exact 32- or 64-character " +
                    "uppercase hexadecimal token printed by authority-recovery list.");
            try
            {
                client.Retry(confirmationToken, cancellationToken);
            }
            catch (AuthorityRecoveryClientException exception)
            {
                if (exception.Code == "invalid_confirmation")
                    throw new CliUsageException(
                        "authority-recovery retry requires a valid confirmation token.");
                throw Failure(exception);
            }
            await output.WriteLineAsync(
                $"Recovered AppContainer authority transaction {confirmationToken}.");
            return 0;
        }

        throw new CliUsageException(
            "Usage: gbar authority-recovery list | " +
            "gbar authority-recovery retry <confirmation-token>");
    }

    private static CliOperationException Failure(
        AuthorityRecoveryClientException exception)
    {
        var code = exception.Code is "stale_confirmation" or "recovery_not_verified"
            ? exception.Code
            : "recovery_unavailable";
        return new CliOperationException(
            $"Authority recovery failed ({code}). " +
            "Run 'gbar authority-recovery list' to inspect current pending transactions.",
            exception);
    }

    private static bool IsSafe(AuthorityRecoverySummary candidate) =>
        IsConfirmationToken(candidate.ConfirmationToken) &&
        candidate.ProfileName is { Length: > 0 and <= 128 } &&
        candidate.ProfileName.All(character =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or
                >= '0' and <= '9' or '.' or '-' or '_') &&
        candidate.TargetCount > 0;

    private static bool IsConfirmationToken(string value) =>
        value is { Length: 32 or 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private sealed class RuntimeAuthorityRecoveryClient(
        IAppContainerAuthorityRecoveryService service) : IAuthorityRecoveryClient
    {
        internal static RuntimeAuthorityRecoveryClient Instance { get; } =
            new(AppContainerAuthorityRecoveryService.Default);

        public IReadOnlyList<AuthorityRecoverySummary> ListPending(
            CancellationToken cancellationToken = default)
        {
            try
            {
                return service.ListPending(cancellationToken)
                    .Select(candidate => new AuthorityRecoverySummary(
                        candidate.ConfirmationToken,
                        candidate.ProfileName,
                        candidate.TargetCount,
                        candidate.IsLegacy))
                    .ToArray();
            }
            catch (AppContainerAuthorityRecoveryException exception)
            {
                throw new AuthorityRecoveryClientException(
                    exception.Code, exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new AuthorityRecoveryClientException(
                    "recovery_unavailable", exception);
            }
        }

        public void Retry(
            string confirmationToken,
            CancellationToken cancellationToken = default)
        {
            try
            {
                service.Retry(confirmationToken, cancellationToken);
            }
            catch (AppContainerAuthorityRecoveryException exception)
            {
                throw new AuthorityRecoveryClientException(
                    exception.Code, exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new AuthorityRecoveryClientException(
                    "recovery_unavailable", exception);
            }
        }
    }
}
