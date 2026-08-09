using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.WidgetBridge;

public sealed record WorkerResidencyBudgetOptions
{
    public const int DefaultMaximumApplicationWorkers = 8;
    public const int DefaultMaximumApplicationMemoryMb = 512;

    public int MaximumApplicationWorkers { get; init; } = DefaultMaximumApplicationWorkers;
    public int MaximumApplicationMemoryMb { get; init; } = DefaultMaximumApplicationMemoryMb;

    internal void Validate()
    {
        if (MaximumApplicationWorkers is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(MaximumApplicationWorkers));
        if (MaximumApplicationMemoryMb is < 16 or > 16_384)
            throw new ArgumentOutOfRangeException(nameof(MaximumApplicationMemoryMb));
    }
}

public readonly record struct WorkerResidencyBudgetSnapshot(
    int ApplicationWorkers,
    int ApplicationMemoryMb,
    int MaximumApplicationWorkers,
    int MaximumApplicationMemoryMb,
    int ControlPlaneWorkers,
    int ControlPlaneMemoryMb)
{
    public int TotalWorkers => ApplicationWorkers + ControlPlaneWorkers;
    public int TotalMemoryMb => ApplicationMemoryMb + ControlPlaneMemoryMb;
}

internal sealed class WorkerResidencyBudget
{
    private readonly object _gate = new();
    private readonly WorkerResidencyBudgetOptions _options;
    private readonly Dictionary<object, Reservation> _reservations =
        new(ReferenceEqualityComparer.Instance);
    private int _applicationWorkers;
    private int _applicationMemoryMb;
    private int _controlPlaneWorkers;
    private int _controlPlaneMemoryMb;

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
                    _applicationMemoryMb,
                    _options.MaximumApplicationWorkers,
                    _options.MaximumApplicationMemoryMb,
                    _controlPlaneWorkers,
                    _controlPlaneMemoryMb);
            }
        }
    }

    public IDisposable Reserve(object owner, string widgetId, int memoryMb, bool isControlPlane)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        if (memoryMb is < 16 or > 256) throw new ArgumentOutOfRangeException(nameof(memoryMb));

        lock (_gate)
        {
            if (_reservations.ContainsKey(owner))
                throw new InvalidOperationException("Worker owner already holds a residency reservation.");
            if (isControlPlane)
            {
                if (_controlPlaneWorkers != 0)
                    throw new WidgetProcessAdmissionException(
                        "The trusted Settings control-plane worker is already resident.");
                _reservations.Add(owner, new Reservation(memoryMb, IsControlPlane: true));
                _controlPlaneWorkers++;
                _controlPlaneMemoryMb += memoryMb;
                return new ReservationLease(this, owner);
            }

            if (_applicationWorkers >= _options.MaximumApplicationWorkers)
                throw CapacityException(widgetId,
                    $"the application worker limit ({_applicationWorkers}/{_options.MaximumApplicationWorkers})");
            if (_applicationMemoryMb > _options.MaximumApplicationMemoryMb - memoryMb)
                throw CapacityException(widgetId,
                    $"the application memory limit ({_applicationMemoryMb}+{memoryMb}/" +
                    $"{_options.MaximumApplicationMemoryMb} MiB)");

            _reservations.Add(owner, new Reservation(memoryMb, IsControlPlane: false));
            _applicationWorkers++;
            _applicationMemoryMb += memoryMb;
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
                _controlPlaneMemoryMb -= reservation.MemoryMb;
            }
            else
            {
                _applicationWorkers--;
                _applicationMemoryMb -= reservation.MemoryMb;
            }
        }
    }

    private static WidgetProcessAdmissionException CapacityException(string widgetId, string limit) =>
        new($"Worker residency budget is full. Launching widget '{widgetId}' would exceed {limit}. " +
            "Disable a resident widget or wait for an unload-after-idle worker before retrying.");

    private sealed record Reservation(int MemoryMb, bool IsControlPlane);

    private sealed class ReservationLease(WorkerResidencyBudget owner, object reservation) : IDisposable
    {
        private WorkerResidencyBudget? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(reservation);
    }
}
