using Application.Abstractions.DataSource;
using Application.Abstractions.Events;
using Application.Abstractions.Time;
using Application.Caches;
using Application.Configuration;
using Application.DTOs.Alarms;
using Application.DTOs.MeasurementMap;
using Application.DTOs.UI;
using Application.Events;
using Application.Pipelines;
using AppLogging;
using Domain.Measurements;
using Domain.Tags;

namespace Application.Services;

public sealed class MonitoringRuntimeService
{
    private readonly DataSourceService _dataSourceService;
    private readonly DataCleanPipeline _dataCleanPipeline;
    private readonly AlarmService _alarmService;
    private readonly IApplicationEventPublisher _eventPublisher;
    private readonly IClock _clock;
    private readonly DataSourceHealthMonitor _healthMonitor;
    private readonly IRuntimeOptionsStore _runtimeOptionsStore;
    private readonly ProcessedFrameSnapshotStore? _processedFrameStore;
    private readonly MeasurementMapService? _measurementMapService;

    public MonitoringRuntimeService(
        DataSourceService dataSourceService,
        DataCleanPipeline dataCleanPipeline,
        AlarmService alarmService,
        IApplicationEventPublisher eventPublisher,
        IClock clock,
        DataSourceHealthMonitor? healthMonitor = null,
        IRuntimeOptionsStore? runtimeOptionsStore = null,
        ProcessedFrameSnapshotStore? processedFrameStore = null,
        MeasurementMapService? measurementMapService = null)
    {
        _dataSourceService = dataSourceService;
        _dataCleanPipeline = dataCleanPipeline;
        _alarmService = alarmService;
        _eventPublisher = eventPublisher;
        _clock = clock;
        _healthMonitor = healthMonitor ?? new DataSourceHealthMonitor(clock);
        _runtimeOptionsStore = runtimeOptionsStore ?? new RuntimeOptionsStore(new MonitorRuntimeOptions());
        _processedFrameStore = processedFrameStore;
        _measurementMapService = measurementMapService;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        AppLogger.Info("MonitoringRuntimeService | RunAsync");
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runtimeToken = sessionCancellation.Token;
        _dataCleanPipeline.ResetSession();
        var sessionId = _healthMonitor.StartSession();
        IReadOnlyList<CleanedTagValue> lastCleanedValues = [];
        var timeout = _runtimeOptionsStore.Snapshot.DataSourceTimeout;
        var enumerator = _dataSourceService
            .ReadFramesAsync(runtimeToken)
            .GetAsyncEnumerator(runtimeToken);
        Task<bool>? moveNextTask = null;

        try
        {
            while (!runtimeToken.IsCancellationRequested)
            {
                moveNextTask ??= enumerator.MoveNextAsync().AsTask();
                var watchdogSnapshot = _healthMonitor.Snapshot(sessionId);

                if (watchdogSnapshot.IsTimedOut || lastCleanedValues.Count == 0)
                {
                    if (!await moveNextTask.ConfigureAwait(false)) break;
                    var frame = enumerator.Current;
                    moveNextTask = null;
                    lastCleanedValues = await ProcessFrameAsync(sessionId, frame, runtimeToken).ConfigureAwait(false);
                    continue;
                }

                var remainingDelay = _healthMonitor.GetRemainingDelay(watchdogSnapshot, timeout);
                using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(runtimeToken);
                var delayTask = _healthMonitor.DelayAsync(remainingDelay, delayCancellation.Token);
                await Task.WhenAny(moveNextTask, delayTask).ConfigureAwait(false);

                // A completed frame always wins over a stale timeout calculation.
                if (moveNextTask.IsCompleted)
                {
                    delayCancellation.Cancel();
                    await IgnoreExpectedCancellationAsync(delayTask).ConfigureAwait(false);
                    if (!await moveNextTask.ConfigureAwait(false)) break;
                    var frame = enumerator.Current;
                    moveNextTask = null;
                    lastCleanedValues = await ProcessFrameAsync(sessionId, frame, runtimeToken).ConfigureAwait(false);
                    continue;
                }

                await delayTask.ConfigureAwait(false);
                var timedOutEvent = _healthMonitor.TryMarkTimedOut(watchdogSnapshot, timeout);
                if (timedOutEvent is null) continue;

                await _eventPublisher.PublishAsync(timedOutEvent, runtimeToken).ConfigureAwait(false);
                await PublishOfflineStatesAsync(
                    timedOutEvent,
                    lastCleanedValues,
                    runtimeToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _healthMonitor.StopSession(sessionId);
            sessionCancellation.Cancel();
            if (moveNextTask is not null)
            {
                try { await moveNextTask.ConfigureAwait(false); }
                catch (OperationCanceledException) when (runtimeToken.IsCancellationRequested) { }
            }
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task IgnoreExpectedCancellationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private async Task<IReadOnlyList<CleanedTagValue>> ProcessFrameAsync(
        long sessionId,
        RawMeasurementFrame frame,
        CancellationToken cancellationToken)
    {
        var recoveredEvent = _healthMonitor.RecordFrame(sessionId, frame);
        await _eventPublisher.PublishAsync(new RawFrameReceivedEvent(frame), cancellationToken).ConfigureAwait(false);
        if (recoveredEvent is not null)
        {
            await _eventPublisher.PublishAsync(recoveredEvent, cancellationToken).ConfigureAwait(false);
        }

        var cleanedValues = _dataCleanPipeline.CleanToCleanedValues(frame);
        var states = await PublishEvaluatedStatesAsync(
            frame.FrameId,
            frame.SequenceNo,
            frame.Timestamp,
            cleanedValues,
            cancellationToken).ConfigureAwait(false);
        var matrixAnalysis = frame.MatrixValues is null || _measurementMapService is null
            ? null
            : _measurementMapService.Analyze(frame.MatrixValues with
            {
                SourceFrameId = frame.FrameId,
                SequenceNo = frame.SequenceNo
            });
        CommitProcessedSnapshot(
            frame.FrameId,
            frame.SequenceNo,
            frame.Timestamp,
            states,
            matrixAnalysis);
        return cleanedValues;
    }

    private async Task PublishOfflineStatesAsync(
        DataSourceTimedOutEvent timedOutEvent,
        IReadOnlyList<CleanedTagValue> lastCleanedValues,
        CancellationToken cancellationToken)
    {
        var timeoutTimestamp = new DateTimeOffset(timedOutEvent.TimedOutAtUtc);
        var timeoutTransitionId = Guid.NewGuid();
        var offlineValues = lastCleanedValues.Select(value => value with
        {
            Timestamp = timeoutTimestamp,
            Quality = TagQuality.Offline,
            SourceFrameId = timeoutTransitionId,
            CleanMessage = "Data source timed out; last value retained."
        }).ToArray();
        var states = await PublishEvaluatedStatesAsync(
            timeoutTransitionId,
            timedOutEvent.LastSequenceNo,
            timedOutEvent.TimedOutAtUtc,
            offlineValues,
            cancellationToken).ConfigureAwait(false);
        var retainedMatrix = CreateRetainedMatrixSnapshot(
            _processedFrameStore?.GetLatest()?.MatrixAnalysis,
            timeoutTransitionId,
            timedOutEvent.LastSequenceNo,
            timedOutEvent.TimedOutAtUtc);
        CommitProcessedSnapshot(
            timeoutTransitionId,
            timedOutEvent.LastSequenceNo,
            timedOutEvent.TimedOutAtUtc,
            states,
            retainedMatrix);
    }

    private async Task<IReadOnlyList<TagRuntimeState>> PublishEvaluatedStatesAsync(
        Guid frameId,
        long sequenceNo,
        DateTime timestampUtc,
        IReadOnlyList<CleanedTagValue> cleanedValues,
        CancellationToken cancellationToken)
    {
        var alarmEvaluation = _alarmService.EvaluateWithChanges(
            cleanedValues,
            new DateTimeOffset(_clock.UtcNow));
        await _eventPublisher.PublishAsync(
            new TagRuntimeStatesProducedEvent(frameId, sequenceNo, timestampUtc, alarmEvaluation.States),
            cancellationToken).ConfigureAwait(false);

        foreach (var lifecycleChange in alarmEvaluation.LifecycleChanges)
        {
            await PublishAlarmChangeAsync(lifecycleChange, cancellationToken).ConfigureAwait(false);
        }

        return alarmEvaluation.States;
    }

    private void CommitProcessedSnapshot(
        Guid sourceFrameId,
        long sequenceNo,
        DateTime timestampUtc,
        IReadOnlyList<TagRuntimeState> states,
        MatrixAnalysisSnapshotDto? matrixAnalysis)
    {
        _processedFrameStore?.Update(new ProcessedFrameSnapshot(
            sourceFrameId,
            sequenceNo,
            timestampUtc,
            states,
            matrixAnalysis));
    }

    private static MatrixAnalysisSnapshotDto? CreateRetainedMatrixSnapshot(
        MatrixAnalysisSnapshotDto? analysis,
        Guid sourceFrameId,
        long sequenceNo,
        DateTime timestampUtc) =>
        analysis is null
            ? null
            : analysis with
            {
                Timestamp = timestampUtc,
                SourceFrameId = sourceFrameId,
                SequenceNo = sequenceNo,
                Frame = analysis.Frame with
                {
                    Timestamp = timestampUtc,
                    SourceFrameId = sourceFrameId,
                    SequenceNo = sequenceNo
                }
            };

    private async ValueTask PublishAlarmChangeAsync(
        AlarmLifecycleChange lifecycleChange,
        CancellationToken cancellationToken)
    {
        switch (lifecycleChange.ChangeType)
        {
            case AlarmLifecycleChangeType.Raised:
                await _eventPublisher.PublishAsync(new AlarmRaisedEvent(lifecycleChange.Alarm), cancellationToken).ConfigureAwait(false);
                break;
            case AlarmLifecycleChangeType.Updated:
                await _eventPublisher.PublishAsync(new AlarmUpdatedEvent(lifecycleChange.Alarm), cancellationToken).ConfigureAwait(false);
                break;
            case AlarmLifecycleChangeType.Recovered:
                await _eventPublisher.PublishAsync(new AlarmRecoveredEvent(lifecycleChange.Alarm), cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lifecycleChange), lifecycleChange.ChangeType, "Unsupported alarm lifecycle change.");
        }
    }
}
