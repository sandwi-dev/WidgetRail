namespace GameBarAlternative.PlatformBroker;

internal sealed class AppLibraryArtworkRegistry
{
    internal const int MaximumRegistrationsPerSession = 512;
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

    private IReadOnlyDictionary<string, string> Replace(
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

            var previous = session.ByProviderIdentity;
            var next = new Dictionary<string, HandleRegistration>(StringComparer.Ordinal);
            var projected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var key = new RegistrationIdentity(
                    item.ProviderAppId, item.StableProviderIdentity);
                var handle = previous.TryGetValue(item.ProviderAppId, out var existing) &&
                    existing.Key == key
                        ? existing.Handle
                        : NewHandle();
                next.Add(item.ProviderAppId, new HandleRegistration(handle, key));
                projected.Add(item.ProviderAppId, handle);
            }

            foreach (var old in previous.Values)
                if (!next.Values.Any(item => item.Handle == old.Handle))
                    _registrations.Remove(old.Handle);
            foreach (var item in next.Values)
                _registrations[item.Handle] = new Registration(
                    session, item.Key.ProviderAppId, item.Key.StableProviderIdentity);
            session.ByProviderIdentity = next;
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
        internal Dictionary<string, HandleRegistration> ByProviderIdentity { get; set; } =
            new(StringComparer.Ordinal);
    }

    private sealed record Registration(
        Session Session,
        string ProviderAppId,
        string StableProviderIdentity);
    internal sealed record RegistrationIdentity(
        string ProviderAppId,
        string StableProviderIdentity);
    internal sealed record HandleRegistration(string Handle, RegistrationIdentity Key);

    internal sealed class AppLibraryArtworkSession(
        AppLibraryArtworkRegistry owner,
        Session session) : IDisposable
    {
        private AppLibraryArtworkRegistry? _owner = owner;
        internal IReadOnlyDictionary<string, string> Replace(
            IReadOnlyList<AppLibraryBackendItemSummary> items) =>
            (_owner ?? throw new ObjectDisposedException(nameof(AppLibraryArtworkSession)))
                .Replace(session, items);
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.End(session);
    }
}
