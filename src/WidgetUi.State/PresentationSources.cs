using System.Collections.ObjectModel;

namespace WidgetRail.WidgetUi.State;

public enum PresentationSurfaceKind { Background, FocusFragment }

/// <summary>One presentation authority. Replace RuntimeId when a worker is replaced.</summary>
public sealed record PresentationAuthority(string RuntimeId, string WidgetInstanceId, string PresentationId);

/// <summary>Slot ownership is logical, never derived from a recycled visual instance.</summary>
public sealed record PresentationSurfaceId(string ScopeId, string ElementId, PresentationSurfaceKind Kind);

/// <summary>
/// Identity excludes changing content. ItemIdentity distinguishes item-key ancestry;
/// Role distinguishes reuse of the same element ID for a different kind of control.
/// </summary>
public sealed record PresentationSourceId(string ScopeId, string ElementId, string Role, string ItemIdentity = "");

public sealed record PresentationSurface<TContent>(
    PresentationSurfaceId Id, TContent DefaultContent, bool? RetainLastSource = null) where TContent : notnull
{
    public bool RetainsSource => RetainLastSource ?? Id.Kind == PresentationSurfaceKind.Background;
}

/// <summary>
/// A contribution admitted from current logical data. The caller must resolve its
/// nearest owning surface and omit hidden, removed or otherwise ineligible sources.
/// Unrealized controls remain eligible. Content must be an immutable value.
/// </summary>
public sealed record PresentationContribution<TContent>(
    PresentationSourceId Source, PresentationSurfaceId Owner, TContent Content) where TContent : notnull;

/// <summary>
/// Validated immutable membership for one revision. Copies caller collections;
/// payload values must themselves be immutable. Contains no live UI objects.
/// </summary>
public sealed class PresentationRevision<TContent> where TContent : notnull
{
    public PresentationAuthority Authority { get; }
    public IReadOnlyList<PresentationSurface<TContent>> Surfaces { get; }
    public IReadOnlyList<PresentationContribution<TContent>> Contributions { get; }
    internal IReadOnlyDictionary<(PresentationSurfaceId Owner, PresentationSourceId Source), TContent> Values { get; }

    public PresentationRevision(PresentationAuthority authority,
        IEnumerable<PresentationSurface<TContent>> surfaces,
        IEnumerable<PresentationContribution<TContent>> contributions)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(contributions);
        Require(authority.RuntimeId, nameof(authority));
        Require(authority.WidgetInstanceId, nameof(authority));
        Require(authority.PresentationId, nameof(authority));
        var surfaceArray = surfaces.ToArray();
        var contributionArray = contributions.ToArray();
        var owners = new HashSet<PresentationSurfaceId>();
        foreach (var surface in surfaceArray)
        {
            ArgumentNullException.ThrowIfNull(surface);
            Validate(surface.Id);
            ArgumentNullException.ThrowIfNull(surface.DefaultContent);
            if (!owners.Add(surface.Id))
                throw new ArgumentException("Presentation surface identities must be unique.", nameof(surfaces));
        }
        var values = new Dictionary<(PresentationSurfaceId, PresentationSourceId), TContent>();
        var ownership = new HashSet<(PresentationSourceId, PresentationSurfaceKind)>();
        var sourceElements = new Dictionary<(string Scope, string Element), PresentationSourceId>();
        foreach (var contribution in contributionArray)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            ArgumentNullException.ThrowIfNull(contribution.Source);
            Validate(contribution.Owner);
            var source = contribution.Source;
            Require(source.ScopeId, nameof(contributions));
            Require(source.ElementId, nameof(contributions));
            Require(source.Role, nameof(contributions));
            ArgumentNullException.ThrowIfNull(source.ItemIdentity);
            ArgumentNullException.ThrowIfNull(contribution.Content);
            if (!owners.Contains(contribution.Owner))
                throw new ArgumentException("A contribution must reference an existing owning surface.", nameof(contributions));
            if (!ownership.Add((source, contribution.Owner.Kind)))
                throw new ArgumentException("A source can contribute to only one surface of each kind.", nameof(contributions));
            var element = (source.ScopeId, source.ElementId);
            if (sourceElements.TryGetValue(element, out var other) && other != source)
                throw new ArgumentException("An element cannot have conflicting logical identities in one scope.", nameof(contributions));
            sourceElements[element] = source;
            values.Add((contribution.Owner, source), contribution.Content);
        }
        Authority = authority;
        Surfaces = Array.AsReadOnly(surfaceArray);
        Contributions = Array.AsReadOnly(contributionArray);
        Values = new ReadOnlyDictionary<(PresentationSurfaceId, PresentationSourceId), TContent>(values);
    }

    private static void Validate(PresentationSurfaceId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Require(id.ScopeId, nameof(id));
        Require(id.ElementId, nameof(id));
        if (!Enum.IsDefined(id.Kind)) throw new ArgumentOutOfRangeException(nameof(id));
    }

    private static void Require(string value, string name) => ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
}

/// <summary>Null Source means the current authored default was selected.</summary>
public sealed record PresentationSelection<TContent>(
    PresentationSurfaceId Surface, PresentationSourceId? Source, TContent Content) where TContent : notnull;

/// <summary>
/// Semantic source memory for one frontend presentation session. Calls are serialized
/// by the owner. No retained controls, layout, scrolling, artwork requests or actions.
/// </summary>
public sealed class PresentationSourceCoordinator<TContent> where TContent : notnull
{
    private PresentationAuthority? _authority;
    private Dictionary<PresentationSurfaceId, PresentationSourceId> _remembered = [];

    /// <summary>
    /// Resolves all surfaces against one admitted revision before committing memory.
    /// The returned read-only list and previous results are unaffected by later calls.
    /// A source removed from membership loses retention permanently until focused again.
    /// </summary>
    public IReadOnlyList<PresentationSelection<TContent>> Resolve(
        PresentationRevision<TContent> revision, PresentationSourceId? focusedSource)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var sameAuthority = revision.Authority == _authority;
        var nextMemory = new Dictionary<PresentationSurfaceId, PresentationSourceId>();
        var result = new PresentationSelection<TContent>[revision.Surfaces.Count];
        for (var i = 0; i < revision.Surfaces.Count; ++i)
        {
            var surface = revision.Surfaces[i];
            PresentationSourceId? selected = null;
            var content = surface.DefaultContent;
            if (focusedSource is not null && revision.Values.TryGetValue((surface.Id, focusedSource), out var focusedContent))
            {
                selected = focusedSource;
                content = focusedContent;
            }
            else if (sameAuthority && surface.RetainsSource &&
                _remembered.TryGetValue(surface.Id, out var remembered) &&
                revision.Values.TryGetValue((surface.Id, remembered), out var retainedContent))
            {
                selected = remembered;
                content = retainedContent;
            }
            if (selected is not null && surface.RetainsSource) nextMemory.Add(surface.Id, selected);
            result[i] = new(surface.Id, selected, content);
        }
        _authority = revision.Authority;
        _remembered = nextMemory;
        return Array.AsReadOnly(result);
    }

    public void Clear()
    {
        _authority = null;
        _remembered.Clear();
    }

    /// <summary>Retires one slot when its enclosing declaration ownership changes.</summary>
    public void Forget(PresentationSurfaceId surface) => _remembered.Remove(surface);
}
