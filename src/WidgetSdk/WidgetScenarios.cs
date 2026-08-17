namespace WidgetRail.WidgetSdk;

/// <summary>
/// One credential-free semantic preview definition. A declared static scenario
/// factory returns this value inside the isolated preview worker; the CLI never
/// loads the scenario assembly.
/// </summary>
public sealed record WidgetScenarioDefinition
{
    public const int CurrentVersion = 1;

    public WidgetScenarioDefinition(
        Widget widget,
        WidgetHostServices hostServices,
        int version = CurrentVersion)
    {
        if (version != CurrentVersion)
            throw new ArgumentOutOfRangeException(nameof(version));
        Widget = widget ?? throw new ArgumentNullException(nameof(widget));
        HostServices = hostServices ?? throw new ArgumentNullException(nameof(hostServices));
        Version = version;
    }

    public int Version { get; }
    public Widget Widget { get; }
    public WidgetHostServices HostServices { get; }
}

/// <summary>A bounded, portable result emitted by isolated semantic preview.</summary>
public sealed record WidgetScenarioResult
{
    public const int CurrentVersion = 1;

    public WidgetScenarioResult(
        int version,
        string scenario,
        WidgetRail.WidgetProtocol.ViewSnapshot snapshot,
        IReadOnlyList<WidgetScenarioDiagnostic> diagnostics)
    {
        if (version != CurrentVersion)
            throw new ArgumentOutOfRangeException(nameof(version));
        if (string.IsNullOrWhiteSpace(scenario) || scenario.Length > 64 ||
            !char.IsAsciiLetterOrDigit(scenario[0]) ||
            scenario.Any(character => !(char.IsAsciiLetterOrDigit(character) ||
                character == '-')))
            throw new ArgumentException("Scenario name is invalid.", nameof(scenario));
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count > 16 || diagnostics.Any(item => item is null))
            throw new ArgumentException(
                "Scenario diagnostics exceed the bounded contract.", nameof(diagnostics));
        Version = version;
        Scenario = scenario;
        Snapshot = snapshot;
        Diagnostics = diagnostics.ToArray();
    }

    public int Version { get; }
    public string Scenario { get; }
    public WidgetRail.WidgetProtocol.ViewSnapshot Snapshot { get; }
    public IReadOnlyList<WidgetScenarioDiagnostic> Diagnostics { get; }
}

/// <summary>One sanitized scenario-worker diagnostic.</summary>
public sealed record WidgetScenarioDiagnostic
{
    public WidgetScenarioDiagnostic(string stage, string code)
    {
        Stage = Validate(stage, nameof(stage));
        Code = Validate(code, nameof(code));
    }

    public string Stage { get; }
    public string Code { get; }

    private static string Validate(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) ||
                character is '_' or '-')))
            throw new ArgumentException(
                "Scenario diagnostics must be bounded safe codes.", parameter);
        return value;
    }
}
