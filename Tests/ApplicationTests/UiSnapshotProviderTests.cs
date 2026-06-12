using Application.Caches;
using Application.DTOs.MeasurementMap;
using Application.DTOs.UI;
using Application.Services;
using Domain.Measurements;
using Domain.Tags;
using Tests.Support;

namespace Tests.ApplicationTests;

public class UiSnapshotProviderTests
{
    [Fact]
    public void GetSnapshot_UsesSharedTagSnapshotAndMatchingMatrixSource()
    {
        var sourceFrameId = Guid.NewGuid();
        const long sequenceNo = 42;
        var timestamp = DateTimeOffset.UtcNow;
        var clock = new TestClock(timestamp.UtcDateTime);
        var tagService = new TagService(new TagCache(100), clock);
        tagService.UpdateTags([
            CreateState("MEAS.TEMP.CH01", 25, timestamp, sourceFrameId, sequenceNo),
            CreateState("MEAS.PRESSURE.CH01", 101, timestamp, sourceFrameId, sequenceNo)
        ]);
        var alarmService = new AlarmService();
        var dashboardService = new DashboardService(tagService, alarmService, clock);
        var chartService = new ChartDataService(tagService);
        var matrixService = new MeasurementMapService(new MatrixFrameCache());
        matrixService.Update(new MatrixFrame(
            Guid.NewGuid(),
            timestamp.UtcDateTime,
            2,
            2,
            new double[,] { { 1, 2 }, { 3, 4 } },
            sourceFrameId,
            sequenceNo));
        var processedStore = new ProcessedFrameSnapshotStore();
        processedStore.Update(new ProcessedFrameSnapshot(
            sourceFrameId,
            sequenceNo,
            timestamp.UtcDateTime,
            tagService.GetSnapshot().CurrentValues,
            matrixService.GetLatestAnalysis()));
        var provider = new UiSnapshotProvider(
            tagService,
            alarmService,
            dashboardService,
            chartService,
            matrixService,
            clock,
            processedStore);

        var snapshot = provider.GetSnapshot(CreateRequest());

        Assert.Equal(sourceFrameId, snapshot.Dashboard.SourceFrameId);
        Assert.Equal(sequenceNo, snapshot.Dashboard.SequenceNo);
        Assert.Equal(sourceFrameId, snapshot.DashboardTrend.SourceFrameId);
        Assert.Equal(sequenceNo, snapshot.DashboardTrend.SequenceNo);
        Assert.Equal(sourceFrameId, snapshot.SelectedTrend.SourceFrameId);
        Assert.Equal(sequenceNo, snapshot.SelectedTrend.SequenceNo);
        Assert.Equal(TimeSpan.FromMinutes(1), snapshot.SelectedTrend.Window);
        Assert.Equal(25, snapshot.SelectedTrend.CurrentValue);
        Assert.Equal(TagQuality.Good, snapshot.SelectedTrend.CurrentQuality);
        Assert.Equal(sourceFrameId, snapshot.MatrixAnalysis?.SourceFrameId);
        Assert.Equal(sequenceNo, snapshot.MatrixAnalysis?.SequenceNo);
        Assert.Equal(sourceFrameId, snapshot.MeasurementMap?.SourceFrameId);
        Assert.Equal(sequenceNo, snapshot.MeasurementMap?.SequenceNo);
        Assert.Equal(sourceFrameId, snapshot.MatrixPreview?.SourceFrameId);
        Assert.Equal(sequenceNo, snapshot.MatrixPreview?.SequenceNo);
        Assert.True(snapshot.IsFrameConsistent);
    }

    [Fact]
    public void GetSnapshot_CanExcludeProcessedMatrix()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var clock = new TestClock(timestamp.UtcDateTime);
        var tagService = new TagService(new TagCache(100), clock);
        var sourceFrameId = Guid.NewGuid();
        tagService.UpdateTags([CreateState("MEAS.TEMP.CH01", 25, timestamp, sourceFrameId, 10)]);
        var alarmService = new AlarmService();
        var matrixService = new MeasurementMapService(new MatrixFrameCache());
        matrixService.Update(new MatrixFrame(
            Guid.NewGuid(),
            timestamp.UtcDateTime,
            1,
            1,
            new double[,] { { 1 } },
            sourceFrameId,
            10));
        var processedStore = new ProcessedFrameSnapshotStore();
        processedStore.Update(new ProcessedFrameSnapshot(
            sourceFrameId,
            10,
            timestamp.UtcDateTime,
            tagService.GetSnapshot().CurrentValues,
            matrixService.GetLatestAnalysis()));
        var provider = new UiSnapshotProvider(
            tagService,
            alarmService,
            new DashboardService(tagService, alarmService, clock),
            new ChartDataService(tagService),
            matrixService,
            clock,
            processedStore);

        var withMatrix = provider.GetSnapshot(CreateRequest());
        var withoutMatrix = provider.GetSnapshot(CreateRequest(includeMatrix: false));

        Assert.True(withMatrix.IsFrameConsistent);
        Assert.NotNull(withMatrix.MatrixAnalysis);
        Assert.Null(withoutMatrix.MatrixAnalysis);
        Assert.Null(withoutMatrix.MeasurementMap);
        Assert.Null(withoutMatrix.MatrixPreview);
        Assert.True(withoutMatrix.IsFrameConsistent);
    }

    [Fact]
    public void GetSnapshot_ProvidesAtomicAlarmListsForDashboardAndAlarmCenter()
    {
        var definition = new TagDefinition(
            "TEST.TAG",
            "Test",
            TagCategory.Measurement,
            "u",
            AlarmHigh: 20);
        var alarmService = new AlarmService([definition]);
        var timestamp = DateTimeOffset.UtcNow;
        var clock = new TestClock(timestamp.UtcDateTime);
        alarmService.Evaluate([
            new CleanedTagValue(
                "TEST.TAG",
                25,
                null,
                null,
                TagDataType.Double,
                "u",
                timestamp,
                TagQuality.Good,
                "TEST",
                "TEST",
                Guid.NewGuid(),
                1,
                null)
        ], timestamp);
        var tagService = new TagService(new TagCache(10), clock);
        var matrixService = new MeasurementMapService(new MatrixFrameCache());
        var provider = new UiSnapshotProvider(
            tagService,
            alarmService,
            new DashboardService(tagService, alarmService, clock),
            new ChartDataService(tagService),
            matrixService,
            clock,
            new ProcessedFrameSnapshotStore());

        var snapshot = provider.GetSnapshot(CreateRequest(includeMatrix: false));

        var dashboardAlarm = Assert.Single(snapshot.Dashboard.ActiveAlarms);
        var currentAlarm = Assert.Single(snapshot.AlarmCenter.CurrentAlarms);
        Assert.Equal(dashboardAlarm.AlarmId, currentAlarm.AlarmId);
        Assert.Contains(snapshot.AlarmCenter.AllEvents, alarm => alarm.AlarmId == currentAlarm.AlarmId);
    }

    [Fact]
    public void GetSnapshot_WithProcessedStore_IgnoresCrossFrameCurrentCaches()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var clock = new TestClock(timestamp.UtcDateTime);
        var tagService = new TagService(new TagCache(100), clock);
        tagService.UpdateTags([CreateState("MEAS.TEMP.CH01", 99, timestamp.AddSeconds(1), Guid.NewGuid(), 99)]);
        var alarmService = new AlarmService();
        var matrixService = new MeasurementMapService(new MatrixFrameCache());
        matrixService.Update(new MatrixFrame(
            Guid.NewGuid(), timestamp.UtcDateTime, 1, 1, new double[,] { { 99 } }, Guid.NewGuid(), 98));
        var processedSource = Guid.NewGuid();
        const long processedSequence = 42;
        var processedAnalysis = matrixService.Analyze(new MatrixFrame(
            Guid.NewGuid(), timestamp.UtcDateTime, 1, 1, new double[,] { { 42 } }, processedSource, processedSequence));
        var processedStore = new ProcessedFrameSnapshotStore();
        processedStore.Update(new ProcessedFrameSnapshot(
            processedSource,
            processedSequence,
            timestamp.UtcDateTime,
            [CreateState("MEAS.TEMP.CH01", 42, timestamp, processedSource, processedSequence)],
            processedAnalysis));
        var provider = new UiSnapshotProvider(
            tagService,
            alarmService,
            new DashboardService(tagService, alarmService, clock),
            new ChartDataService(tagService),
            matrixService,
            clock,
            processedStore);

        var snapshot = provider.GetSnapshot(CreateRequest());

        Assert.True(snapshot.IsFrameConsistent);
        Assert.Equal(processedSource, snapshot.Dashboard.SourceFrameId);
        Assert.Equal(processedSequence, snapshot.Dashboard.SequenceNo);
        Assert.Equal(42, snapshot.Dashboard.Tags.Single().NumericValue);
        Assert.Equal(processedSource, snapshot.SelectedTrend.SourceFrameId);
        Assert.Empty(snapshot.SelectedTrend.Series.Points);
        Assert.Equal(processedSource, snapshot.MatrixAnalysis?.SourceFrameId);
        Assert.Equal(42, snapshot.MatrixAnalysis?.Frame.Values[0, 0]);
    }

    [Fact]
    public void ProcessedFrameStore_ProtectsMutableMatrixValues()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var sourceFrameId = Guid.NewGuid();
        var matrixService = new MeasurementMapService(new MatrixFrameCache());
        var sourceValues = new double[,] { { 1, 2 } };
        var analysis = matrixService.Analyze(new MatrixFrame(
            Guid.NewGuid(), timestamp.UtcDateTime, 1, 2, sourceValues, sourceFrameId, 1));
        var store = new ProcessedFrameSnapshotStore();
        store.Update(new ProcessedFrameSnapshot(
            sourceFrameId,
            1,
            timestamp.UtcDateTime,
            [CreateState("MEAS.TEMP.CH01", 1, timestamp, sourceFrameId, 1)],
            analysis));

        sourceValues[0, 0] = 100;
        var firstRead = store.GetLatest()!;
        firstRead.MatrixAnalysis!.Frame.Values[0, 0] = 200;
        var secondRead = store.GetLatest()!;

        Assert.Equal(1, secondRead.MatrixAnalysis!.Frame.Values[0, 0]);
    }

    private static UiSnapshotRequest CreateRequest(bool includeMatrix = true) =>
        new(
            "MEAS.TEMP.CH01",
            18,
            "MEAS.TEMP.CH01",
            TimeSpan.FromMinutes(1),
            120,
            new MatrixDisplayOptionsDto(),
            includeMatrix);

    private static TagRuntimeState CreateState(
        string tagId,
        double value,
        DateTimeOffset timestamp,
        Guid sourceFrameId,
        long sequenceNo) =>
        new(
            tagId,
            tagId,
            TagCategory.Measurement,
            value,
            null,
            null,
            "u",
            TagDataType.Double,
            TagQuality.Good,
            TagAlarmState.Normal,
            timestamp,
            sourceFrameId,
            sequenceNo,
            timestamp);
}
