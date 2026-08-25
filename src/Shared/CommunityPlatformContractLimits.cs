namespace WidgetRail.Internal;

internal static class CommunityPlatformContractLimits
{
    internal const int MinimumLoopbackPort = 1_024;
    internal const int MaximumLoopbackPort = 65_535;
    internal const int MaximumLoopbackPathCharacters = 2_048;
    internal const int MaximumLoopbackHeaderCount = 16;
    internal const int MaximumLoopbackHeaderNameCharacters = 64;
    internal const int MaximumLoopbackHeaderValueCharacters = 1_024;
    internal const int MaximumLoopbackHeaderCharacters = 8_192;
    internal const int MaximumLoopbackRequestBodyUtf8Bytes = 16 * 1_024;
    internal const int MaximumLoopbackResponseBodyUtf8Bytes = 96 * 1_024;
    internal const int DefaultLoopbackTimeoutMilliseconds = 10_000;
    internal const int MaximumLoopbackTimeoutMilliseconds = 40_000;
    internal const int MaximumPrivateSecretSlotCharacters = 64;
    internal const int MaximumPrivateSecretUtf8Bytes = 2_048;
    internal const int MaximumPrivateStateUtf8Bytes = 64 * 1_024;
    internal const int MaximumPrivateStateBase64Characters =
        ((MaximumPrivateStateUtf8Bytes + 2) / 3) * 4;
}
