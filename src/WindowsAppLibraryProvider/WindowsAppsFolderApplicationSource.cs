using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GameBarAlternative.WindowsAppLibraryProvider;

/// <summary>
/// Enumerates the current user's AppsFolder through the Windows Shell on the
/// provider's STA lane. Only display text plus a trusted internal AUMID are
/// retained; no Shell object or identifier crosses the provider boundary.
/// </summary>
internal sealed class WindowsAppsFolderApplicationSource : IAppsFolderApplicationSource
{
    internal const int MaximumCandidates = 2048;
    // APPLICATION_USER_MODEL_ID_MAX_LENGTH is 130 including the terminator.
    internal const int MaximumAumidLength = 129;
    private const string AppsFolderParsingName = "shell:AppsFolder";
    private const string AppUserModelIdProperty = "System.AppUserModel.ID";

    public IReadOnlyList<AppsFolderRegistration> Enumerate(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return [];
        object? shell = null;
        object? folder = null;
        object? items = null;
        var registrations = new List<AppsFolderRegistration>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: true);
            shell = Activator.CreateInstance(shellType!);
            if (shell is null) throw CollectionUnavailable();
            folder = ((dynamic)shell).NameSpace(AppsFolderParsingName);
            if (folder is null) throw CollectionUnavailable();
            items = ((dynamic)folder).Items();
            if (items is null) throw CollectionUnavailable();
            var count = Math.Clamp(Convert.ToInt32(
                ((dynamic)items).Count, CultureInfo.InvariantCulture), 0, MaximumCandidates);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(index);
                    if (item is null) continue;
                    if (TryReadItem(item) is { } registration)
                        registrations.Add(registration);
                }
                catch (Exception exception) when (IsRecoverableShellFailure(exception))
                {
                    // One malformed/disappearing Shell item cannot poison the catalog.
                }
                finally
                {
                    Release(item);
                }
            }
            return registrations;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableShellFailure(exception))
        {
            throw CollectionUnavailable(exception);
        }
        finally
        {
            Release(items);
            Release(folder);
            Release(shell);
        }
    }

    public AppsFolderRegistration? ReadExact(
        string aumid,
        CancellationToken cancellationToken)
    {
        if (NormalizeAumid(aumid) is not { } expected) return null;
        if (!OperatingSystem.IsWindows()) return null;
        object? shell = null;
        object? folder = null;
        object? item = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: true);
            shell = Activator.CreateInstance(shellType!);
            if (shell is null) throw CollectionUnavailable();
            folder = ((dynamic)shell).NameSpace(AppsFolderParsingName);
            if (folder is null) throw CollectionUnavailable();
            item = ((dynamic)folder).ParseName(expected);
            if (item is null) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var registration = TryReadItem(item);
            return registration is not null && string.Equals(
                    registration.Aumid, expected, StringComparison.OrdinalIgnoreCase)
                ? registration
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableShellFailure(exception))
        {
            throw CollectionUnavailable(exception);
        }
        finally
        {
            Release(item);
            Release(folder);
            Release(shell);
        }
    }

    internal static string? NormalizeAumid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // AUMIDs are opaque launch identifiers. Never normalize or rewrite
        // them: compatibility normalization could turn a rejected identifier
        // into a different application's valid identifier.
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Length is < 1 or > MaximumAumidLength ||
            value.Any(character => !IsAllowedAumidCharacter(character)))
            return null;
        return value;
    }

    private static bool IsAllowedAumidCharacter(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or
            '.' or '-' or '_' or '!' or '{' or '}';

    internal static string IdentityFor(string normalizedAumid)
    {
        var canonical = "appsfolder\0" + normalizedAumid.ToUpperInvariant();
        return "apps-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static AppsFolderRegistration? TryReadItem(object item)
    {
        var displayName = Convert.ToString(
            ((dynamic)item).Name, CultureInfo.InvariantCulture);
        var rawAumid = Convert.ToString(
            ((dynamic)item).ExtendedProperty(AppUserModelIdProperty),
            CultureInfo.InvariantCulture);
        if (NormalizeAumid(rawAumid) is not { } aumid) return null;
        var identity = IdentityFor(aumid);
        return new AppsFolderRegistration(
            identity, displayName ?? string.Empty, aumid, identity);
    }

    private static AppsFolderEnumerationException CollectionUnavailable(
        Exception? innerException = null) =>
        new("Windows AppsFolder could not produce an authoritative collection.",
            innerException);

    private static bool IsRecoverableShellFailure(Exception exception) =>
        exception is COMException or InvalidCastException or InvalidOperationException or
            ArgumentException or NotSupportedException or OverflowException or
            System.Reflection.TargetInvocationException or
            Microsoft.CSharp.RuntimeBinder.RuntimeBinderException;

    private static void Release(object? value)
    {
        if (value is not null && OperatingSystem.IsWindows() && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }
}
