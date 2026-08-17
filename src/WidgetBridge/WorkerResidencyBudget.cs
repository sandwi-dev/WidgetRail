using WidgetRail.WidgetRuntime;

namespace WidgetRail.WidgetBridge;

public sealed record WorkerResidencyBudgetOptions
{
    public const int DefaultMaximumApplicationWorkers = 8;

    public int MaximumApplicationWorkers { get; init; } = DefaultMaximumApplicationWorkers;

    internal void Validate()
    {
        if (MaximumApplicationWorkers is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(MaximumApplicationWorkers));
    }
}

public readonly record struct WorkerResidencyBudgetSnapshot(
    int ApplicationWorkers,
    long ApplicationAdvisoryMemoryMb,
    int MaximumApplicationWorkers,
    int ControlPlaneWorkers,
    long ControlPlaneAdvisoryMemoryMb)
{
    public int TotalWorkers => ApplicationWorkers + ControlPlaneWorkers;
    public long TotalAdvisoryMemoryMb =>
        ApplicationAdvisoryMemoryMb + ControlPlaneAdvisoryMemoryMb;
}

internal sealed class WorkerResidencyBudget
{
    private readonly object _gate = new();
    private readonly WorkerResidencyBudgetOptions _options;
    private readonly Dictionary<object, Reservation> _reservations =
        new(ReferenceEqualityComparer.Instance);
    private int _applicationWorkers;
    private long _applicationAdvisoryMemoryMb;
    private int _controlPlaneWorkers;
    private long _controlPlaneAdvisoryMemoryMb;

    public WorkerResidencyBudget(WorkerResidencyBudgetOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public WorkerResidencyBudgetSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new WorkerResidencyBudgetSnapshot(
                    _applicationWorkers,
                    _applicationAdvisoryMemoryMb,
                    _options.MaximumApplicationWorkers,
                    _controlPlaneWorkers,
                    _controlPlaneAdvisoryMemoryMb);
            }
        }
    }

    public IDisposable Reserve(
        object owner,
        string widgetId,
        int? advisoryMemoryMb,
        bool isControlPlane)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        if (advisoryMemoryMb is <= 0)
            throw new ArgumentOutOfRangeException(nameof(advisoryMemoryMb));
        var reportedMemoryMb = advisoryMemoryMb.GetValueOrDefault();

        lock (_gate)
        {
            if (_reservations.ContainsKey(owner))
                throw new InvalidOperationException("Worker owner already holds a residency reservation.");
            if (isControlPlane)
            {
                if (_controlPlaneWorkers != 0)
                    throw new WidgetProcessAdmissionException(
                        "The trusted Settings control-plane worker is already resident.");
                _reservations.Add(owner, new Reservation(reportedMemoryMb, IsControlPlane: true));
                _controlPlaneWorkers++;
                _controlPlaneAdvisoryMemoryMb += reportedMemoryMb;
                return new ReservationLease(this, owner);
            }

            if (_applicationWorkers >= _options.MaximumApplicationWorkers)
                throw CapacityException(widgetId,
                    $"the application worker limit ({_applicationWorkers}/{_options.MaximumApplicationWorkers})");
            _reservations.Add(owner, new Reservation(reportedMemoryMb, IsControlPlane: false));
            _applicationWorkers++;
            _applicationAdvisoryMemoryMb += reportedMemoryMb;
            return new ReservationLease(this, owner);
        }
    }

    public void Release(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_gate)
        {
            if (!_reservations.Remove(owner, out var reservation)) return;
            if (reservation.IsControlPlane)
            {
                _controlPlaneWorkers--;
                _controlPlaneAdvisoryMemoryMb -= reservation.AdvisoryMemoryMb;
            }
            else
            {
                _applicationWorkers--;
                _applicationAdvisoryMemoryMb -= reservation.AdvisoryMemoryMb;
            }
        }
    }

    private static WidgetProcessAdmissionException CapacityException(string widgetId, string limit) =>
        new($"Worker residency budget is full. Launching widget '{widgetId}' would exceed {limit}. " +
            "Disable a resident widget or wait for an unload-after-idle worker before retrying.");

    private sealed record Reservation(int AdvisoryMemoryMb, bool IsControlPlane);

    private sealed class ReservationLease(WorkerResidencyBudget owner, object reservation) : IDisposable
    {
        private WorkerResidencyBudget? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(reservation);
    }
}
