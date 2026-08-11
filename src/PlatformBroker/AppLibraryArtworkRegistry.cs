namespace GameBarAlternative.PlatformBroker;

internal sealed class AppLibraryArtworkRegistry
{
    internal const int MaximumRegistrationsPerSession = 256;
    private readonly object _gate = new();
    private readonly Dictionary<string, Registration> _registrations =
        new(StringComparer.Ordinal);
    private readonly Dictionary<BrokerWidgetIdentity, Session> _sessions = [];
    private long _sessionSequence;

    internal int RegistrationCount
    {
        get { lock (_gate) return _registrations.Count; }
    }

    internal AppLibraryArtworkSession BeginSession(
        BrokerWidgetIdentity identity,
        IPlatformBrokerBackend backend)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(backend);
        identity.Validate();
        return new AppLibraryArtworkSession(
            this, new Session(identity, backend, Interlocked.Increment(ref _sessionSequence)));
    }

    internal async Task<string?> ResolveAsync(
        BrokerWidgetIdentity identity,
        string handle,
        CancellationToken cancellationToken)
    {
        if (!IsHandle(handle)) return null;
        Registration registration;
        lock (_gate)
        {
            if (!_registrations.TryGetValue(handle, out registration!) ||
                !registration.Session.Identity.Equals(identity) ||
                !_sessions.TryGetValue(identity, out var current) ||
                !ReferenceEquals(current, registration.Session))
                return null;
        }

        AppLibraryIconSummary? icon;
        try
        {
            icon = await registration.Session.Backend.GetAppLibraryIconAsync(
                registration.ProviderAppId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is BrokerException or IOException or
            UnauthorizedAccessException or InvalidOperationException or ArgumentException or
            NotSupportedException)
        {
            return null;
        }

        var validated = BrokerInlinePngPolicy.Validate(
            icon?.PngBase64,
            AppLibraryImageLimits.MaximumPngBytes,
            AppLibraryImageLimits.MaximumPixelDimension);
        if (validated is null) return null;
        lock (_gate)
        {
            return _registrations.TryGetValue(handle, out var current) &&
                ReferenceEquals(current, registration) &&
                _sessions.TryGetValue(identity, out var session) &&
                ReferenceEquals(session, registration.Session)
                    ? validated.Value.Base64
                    : null;
        }
    }

    internal bool IsCurrent(BrokerWidgetIdentity identity, string handle)
    {
        lock (_gate)
            return _registrations.TryGetValue(handle, out var registration) &&
                registration.Session.Identity.Equals(identity) &&
                _sessions.TryGetValue(identity, out var session) &&
                ReferenceEquals(session, registration.Session);
    }

    private IReadOnlyDictionary<string, string> RegisterPage(
        Session session,
        IReadOnlyList<AppLibraryBackendItemSummary> items)
    {
        if (items.Count > MaximumRegistrationsPerSession)
            throw new BrokerException("invalid_backend_data", "Artwork registration is too large.");
        lock (_gate)
        {
            if (_sessions.TryGetValue(session.Identity, out var current) &&
                current.Sequence > session.Sequence)
                return new Dictionary<string, string>(StringComparer.Ordinal);
            if (current is not null && !ReferenceEquals(current, session))
                RemoveSessionLocked(current);
            _sessions[session.Identity] = session;

            var projected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var key = new RegistrationIdentity(
                    item.ProviderAppId, item.StableProviderIdentity, item.ArtworkRevision);
                var handle = session.ByProviderIdentity.TryGetValue(
                        item.ProviderAppId, out var existing) &&
                    existing.Key == key
                        ? existing.Handle
                        : NewHandle();
                if (existing is not null && existing.Handle != handle)
                    _registrations.Remove(existing.Handle);
                session.ByProviderIdentity[item.ProviderAppId] =
                    new HandleRegistration(handle, key);
                session.Touch(item.ProviderAppId);
                projected.Add(item.ProviderAppId, handle);
                _registrations[handle] = new Registration(
                    session, item.ProviderAppId, item.StableProviderIdentity);
            }

            while (session.ByProviderIdentity.Count > MaximumRegistrationsPerSession)
            {
                var evictedId = session.LeastRecent.First!.Value;
                session.LeastRecent.RemoveFirst();
                session.Recency.Remove(evictedId);
                var evicted = session.ByProviderIdentity[evictedId];
                session.ByProviderIdentity.Remove(evictedId);
                _registrations.Remove(evicted.Handle);
            }
            return projected;
        }
    }

    private void End(Session session)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(session.Identity, out var current) &&
                ReferenceEquals(current, session))
                _sessions.Remove(session.Identity);
            RemoveSessionLocked(session);
        }
    }

    private void RemoveSessionLocked(Session session)
    {
        foreach (var registration in session.ByProviderIdentity.Values)
            _registrations.Remove(registration.Handle);
        session.ByProviderIdentity.Clear();
        session.LeastRecent.Clear();
        session.Recency.Clear();
    }

    private static string NewHandle() => "library.art." + Guid.NewGuid().ToString("N");
    internal static bool IsHandle(string? value) =>
        value is { Length: 44 } && value.StartsWith("library.art.", StringComparison.Ordinal) &&
        value.AsSpan(12).ToString().All(character => char.IsAsciiHexDigit(character));

    internal sealed class Session(
        BrokerWidgetIdentity identity,
        IPlatformBrokerBackend backend,
        long sequence)
    {
        internal BrokerWidgetIdentity Identity { get; } = identity;
        internal IPlatformBrokerBackend Backend { get; } = backend;
        internal long Sequence { get; } = sequence;
        internal Dictionary<string, HandleRegistration> ByProviderIdentity { get; } =
            new(StringComparer.Ordinal);
        internal LinkedList<string> LeastRecent { get; } = [];
        internal Dictionary<string, LinkedListNode<string>> Recency { get; } =
            new(StringComparer.Ordinal);

        internal void Touch(string providerAppId)
        {
            if (Recency.Remove(providerAppId, out var existing))
                LeastRecent.Remove(existing);
            Recency[providerAppId] = LeastRecent.AddLast(providerAppId);
        }
    }

    private sealed record Registration(
        Session Session,
        string ProviderAppId,
        string StableProviderIdentity);
    internal sealed record RegistrationIdentity(
        string ProviderAppId,
        string StableProviderIdentity,
        string ArtworkRevision);
    internal sealed record HandleRegistration(string Handle, RegistrationIdentity Key);

    internal sealed class AppLibraryArtworkSession(
        AppLibraryArtworkRegistry owner,
        Session session) : IDisposable
    {
        private AppLibraryArtworkRegistry? _owner = owner;
        internal IReadOnlyDictionary<string, string> RegisterPage(
            IReadOnlyList<AppLibraryBackendItemSummary> items) =>
            (_owner ?? throw new ObjectDisposedException(nameof(AppLibraryArtworkSession)))
                .RegisterPage(session, items);
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.End(session);
    }
}
