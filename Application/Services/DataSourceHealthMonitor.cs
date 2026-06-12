using Application.Abstractions.Time;
using Application.Events;
using Domain.Common;
using Domain.Measurements;

namespace Application.Services;

public sealed class DataSourceHealthMonitor
{
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly object _syncRoot = new();
    private long _sessionId;
    private long _version;
    private long _lastFrameTimestamp;
    private Guid _lastFrameId;
    private long _lastSequenceNo;
    private DateTime _lastFrameTimeUtc;
    private bool _running;
    private bool _timedOut;
    private DataSourceHealthStatus _status = new(DataSourceHealthState.Stopped);

    public event EventHandler<DataSourceHealthStatus>? StatusChanged;

    public DataSourceHealthStatus Status => Volatile.Read(ref _status);

    public DataSourceHealthMonitor(IClock clock, TimeProvider? timeProvider = null)
    {
        _clock = clock;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public long StartSession()
    {
        lock (_syncRoot)
        {
            _sessionId++;
            _version = 0;
            _lastFrameTimestamp = _timeProvider.GetTimestamp();
            _lastFrameId = Guid.Empty;
            _lastSequenceNo = 0;
            _lastFrameTimeUtc = _clock.UtcNow;
            _running = true;
            _timedOut = false;
            SetStatus(new DataSourceHealthStatus(DataSourceHealthState.WaitingForFirstFrame));
            return _sessionId;
        }
    }

    public void StopSession(long sessionId)
    {
        lock (_syncRoot)
        {
            if (_sessionId != sessionId) return;
            _running = false;
            _timedOut = false;
            _version++;
            SetStatus(new DataSourceHealthStatus(DataSourceHealthState.Stopped));
        }
    }

    public DataSourceRecoveredEvent? RecordFrame(long sessionId, RawMeasurementFrame frame)
    {
        MeasurementTimeContract.Validate(frame);
        lock (_syncRoot)
        {
            if (!_running || _sessionId != sessionId) return null;
            var recovered = _timedOut;
            _version++;
            _lastFrameTimestamp = _timeProvider.GetTimestamp();
            _lastFrameId = frame.FrameId;
            _lastSequenceNo = frame.SequenceNo;
            _lastFrameTimeUtc = frame.Timestamp;
            _timedOut = false;
            SetStatus(new DataSourceHealthStatus(
                DataSourceHealthState.Online,
                frame.FrameId,
                frame.SequenceNo,
                frame.Timestamp));
            return recovered
                ? new DataSourceRecoveredEvent(frame.FrameId, frame.SequenceNo, _clock.UtcNow)
                : null;
        }
    }

    public DataSourceWatchdogSnapshot Snapshot(long sessionId)
    {
        lock (_syncRoot)
        {
            return new DataSourceWatchdogSnapshot(
                sessionId,
                _version,
                _running && _sessionId == sessionId,
                _timedOut,
                _lastFrameTimestamp,
                _lastFrameId,
                _lastSequenceNo,
                _lastFrameTimeUtc);
        }
    }

    public DataSourceTimedOutEvent? TryMarkTimedOut(
        DataSourceWatchdogSnapshot expected,
        TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_syncRoot)
        {
            if (!_running
                || _sessionId != expected.SessionId
                || _version != expected.Version
                || _timedOut
                || _lastFrameId == Guid.Empty
                || _timeProvider.GetElapsedTime(_lastFrameTimestamp) < timeout)
            {
                return null;
            }

            _timedOut = true;
            var timedOutEvent = new DataSourceTimedOutEvent(
                _lastFrameId,
                _lastSequenceNo,
                UtcDateTime.Require(_lastFrameTimeUtc, nameof(_lastFrameTimeUtc)),
                _clock.UtcNow);
            SetStatus(new DataSourceHealthStatus(
                DataSourceHealthState.TimedOut,
                _lastFrameId,
                _lastSequenceNo,
                _lastFrameTimeUtc));
            return timedOutEvent;
        }
    }

    public TimeSpan GetRemainingDelay(DataSourceWatchdogSnapshot snapshot, TimeSpan timeout)
    {
        var elapsed = _timeProvider.GetElapsedTime(snapshot.LastFrameMonotonicTimestamp);
        return elapsed >= timeout ? TimeSpan.Zero : timeout - elapsed;
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, _timeProvider, cancellationToken);

    private void SetStatus(DataSourceHealthStatus status)
    {
        var previous = Volatile.Read(ref _status);
        Volatile.Write(ref _status, status);
        if (previous.State == status.State)
        {
            return;
        }
        StatusChanged?.Invoke(this, status);
    }
}

public enum DataSourceHealthState
{
    Stopped,
    WaitingForFirstFrame,
    Online,
    TimedOut
}

public sealed record DataSourceHealthStatus(
    DataSourceHealthState State,
    Guid? LastFrameId = null,
    long? LastSequenceNo = null,
    DateTime? LastFrameTimeUtc = null);

public sealed record DataSourceWatchdogSnapshot(
    long SessionId,
    long Version,
    bool IsRunning,
    bool IsTimedOut,
    long LastFrameMonotonicTimestamp,
    Guid LastFrameId,
    long LastSequenceNo,
    DateTime LastFrameTimeUtc);
