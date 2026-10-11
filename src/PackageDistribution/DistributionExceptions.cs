namespace WidgetRail.PackageDistribution;

public sealed class DistributionUsageException(string message) : Exception(message);
public sealed class DistributionOperationException(string message, Exception? innerException = null) : Exception(message, innerException);
