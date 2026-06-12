namespace Application.Configuration;

public interface IRuntimeOptionsStore
{
    MonitorRuntimeOptions Snapshot { get; }
    void Replace(MonitorRuntimeOptions options);
}

public sealed class RuntimeOptionsStore : IRuntimeOptionsStore
{
    private MonitorRuntimeOptions _snapshot;

    public RuntimeOptionsStore(MonitorRuntimeOptions options)
    {
        _snapshot = options;
    }

    public MonitorRuntimeOptions Snapshot => Volatile.Read(ref _snapshot);

    public void Replace(MonitorRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Volatile.Write(ref _snapshot, options);
    }
}
