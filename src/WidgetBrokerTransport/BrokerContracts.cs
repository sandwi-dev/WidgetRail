using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WidgetRail.PlatformBroker;

public sealed record BrokerWidgetIdentity(
    [property: JsonRequired] string PackageId,
    [property: JsonRequired] string PublisherId,
    [property: JsonRequired] string InstanceId)
{
    public void Validate()
    {
        if (string.IsNullOrEmpty(PackageId) || string.IsNullOrEmpty(PublisherId) ||
            string.IsNullOrEmpty(InstanceId) ||
            !BrokerIdentityValidation.PackageId().IsMatch(PackageId) ||
            !BrokerIdentityValidation.PackageId().IsMatch(PublisherId) ||
            !BrokerIdentityValidation.InstanceId().IsMatch(InstanceId))
            throw new BrokerException("invalid_identity", "Widget identity is invalid.");
    }
}

public enum BrokerLifecycleState
{
    /// <summary>
    /// The authenticated channel exists, but the host has not activated the
    /// widget yet. No capability operation is available in this state.
    /// </summary>
    Created,
    Background,
    Visible,
    Interactive,
    Destroying,
}

public sealed class BrokerException : Exception
{
    public BrokerException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

public static class BluetoothDiscoveryLimits
{
    public const int ScanDurationSeconds = 12;
    public const int CompletionBudgetSeconds = 3;
}

internal static partial class BrokerIdentityValidation
{
    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)+$",
        RegexOptions.CultureInvariant)]
    internal static partial Regex PackageId();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    internal static partial Regex InstanceId();

}
