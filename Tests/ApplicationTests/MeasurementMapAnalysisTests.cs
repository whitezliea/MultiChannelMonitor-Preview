using Application.Caches;
using Application.DTOs.MeasurementMap;
using Application.Pipelines;
using Application.Services;
using Application.Services.MeasurementMap;
using Domain.Devices;
using Domain.Measurements;
using Domain.Tags;
using Simulator.Generators;
using Simulator.Scenarios;

namespace Tests.ApplicationTests;

public sealed class MeasurementMapAnalysisTests
{
    [Fact]
    public void MatrixScaleService_NormalizesAutoAndFixedRanges()
    {
        var service = new MatrixScaleService();
        var statistics = CreateStatistics(min: 10, max: 20);

        var autoRange = service.Resolve(statistics, new MatrixDisplayOptionsDto());
        var fixedRange = service.Resolve(
            statistics,
            new MatrixDisplayOptionsDto(MatrixScaleMode.FixedEngineeringRange, FixedMin: 0, FixedMax: 100));

        Assert.Equal(new ScaleRangeDto(10, 20), autoRange);
        Assert.Equal(0, service.Normalize(5, autoRange));
        Assert.Equal(0.5, service.Normalize(15, autoRange));
        Assert.Equal(1, service.Normalize(25, autoRange));
        Assert.Equal(0.25, service.Normalize(25, fixedRange));
        Assert.Equal(0.5, service.Normalize(10, new ScaleRangeDto(10, 10)));
        Assert.True(double.IsNaN(service.Normalize(double.NaN, autoRange)));
    }

    [Fact]
    public void AbnormalPointDetector_AppliesPriorityAndReturnsOneResultPerPoint()
    {
        var frame = CreateFrame(new double[,]
        {
            { double.NaN, 2000 },
            { 50, 500 }
        });
        var detector = new AbnormalPointDetector();

        var points = detector.Detect(frame, frame.CalculateStatistics());

        Assert.Equal(points.Count, points.Select(point => (point.Row, point.Column)).Distinct().Count());
        Assert.Equal(MatrixAbnormalType.InvalidValue, Find(points, 0, 0).Type);
        Assert.Equal(MatrixAbnormalType.HighLimit, Find(points, 0, 1).Type);
        Assert.Equal(MatrixAbnormalType.LowLimit, Find(points, 1, 0).Type);
        Assert.Equal(MatrixSeverity.Alarm, Find(points, 0, 0).Severity);
        Assert.Equal(MatrixSeverity.Alarm, Find(points, 0, 1).Severity);
        Assert.Equal(MatrixSeverity.Alarm, Find(points, 1, 0).Severity);
    }

    [Fact]
    public void AbnormalPointDetector_DetectsLocalHotspotWhenGlobalRuleIsDisabled()
    {
        var values = new double[5, 5];
        for (var row = 0; row < 5; row++)
        {
            for (var column = 0; column < 5; column++)
            {
                values[row, column] = 500;
            }
        }

        values[2, 2] = 570;
        var frame = CreateFrame(values);
        var options = new MatrixAbnormalDetectionOptions(
            HighLimit: null,
            LowLimit: null,
            ZScoreThreshold: 100,
            LocalStdDevMultiplier: 100,
            LocalRelativeThreshold: 0.12);

        var points = new AbnormalPointDetector().Detect(frame, frame.CalculateStatistics(), options);

        var point = Assert.Single(points);
        Assert.Equal((2, 2), (point.Row, point.Column));
        Assert.Equal(MatrixAbnormalType.LocalHotspot, point.Type);
    }

    [Fact]
    public void NormalSimulatorFrame_DoesNotProduceAbnormalPoints()
    {
        var start = DateTime.UtcNow;
        var frame = new FakeDataGenerator("MCMD-TEST", new NormalScenario(), start)
            .NextFrame(start.AddSeconds(1))
            .MatrixValues!;

        var points = new AbnormalPointDetector().Detect(frame, frame.CalculateStatistics());

        Assert.Empty(points);
    }

    [Fact]
    public void HotspotSimulatorFrame_DetectsPointNearConfiguredHotspot()
    {
        var start = DateTime.UtcNow;
        var frame = new FakeDataGenerator("MCMD-TEST", new MatrixHotspotScenario(), start)
            .NextFrame(start.AddSeconds(1))
            .MatrixValues!;

        var points = new AbnormalPointDetector().Detect(frame, frame.CalculateStatistics());

        Assert.Contains(points, point =>
            Math.Abs(point.Row - 9) <= 1 &&
            Math.Abs(point.Column - 10) <= 1 &&
            point.Type is MatrixAbnormalType.StatisticalHotspot or MatrixAbnormalType.LocalHotspot);
    }

    [Fact]
    public void MeasurementMapSnapshot_MapsScaleCellsQualityAndInvalidValues()
    {
        var service = CreateMeasurementMapService(new MatrixAbnormalDetectionOptions(
            ZScoreThreshold: 100,
            LocalStdDevMultiplier: 100,
            LocalRelativeThreshold: 100));
        service.Update(CreateFrame(new double[,]
        {
            { 500, 600 },
            { double.NaN, 2000 }
        }));

        var snapshot = service.GetLatestSnapshot(new MatrixDisplayOptionsDto(
            MatrixScaleMode.FixedEngineeringRange,
            FixedMin: 0,
            FixedMax: 2000));

        Assert.NotNull(snapshot);
        Assert.Equal("Light Intensity", snapshot.MatrixType);
        Assert.Equal("lux", snapshot.Unit);
        Assert.Equal(new ScaleRangeDto(0, 2000), snapshot.ScaleRange);
        Assert.Equal(4, snapshot.Cells.Count);
        Assert.Equal(2, snapshot.AbnormalPoints.Count);
        Assert.Equal(MatrixQualityState.Alarm, snapshot.QualityState);

        var invalidCell = snapshot.Cells.Single(cell => cell.Row == 1 && cell.Column == 0);
        Assert.False(invalidCell.IsValid);
        Assert.True(invalidCell.IsAbnormal);
        Assert.Equal("NA", invalidCell.DisplayText);
        Assert.Equal(new RgbColorDto(120, 120, 120), invalidCell.Color);

        var maximumCell = snapshot.Cells.Single(cell => cell.Row == 1 && cell.Column == 1);
        Assert.Equal(1, maximumCell.NormalizedValue);
        Assert.Equal(MatrixAbnormalType.HighLimit, maximumCell.AbnormalType);
    }

    [Fact]
    public void MatrixAnalysis_BuildsDetailAndPreviewFromTheSameProcessedFrame()
    {
        var timestamp = DateTime.UtcNow;
        var service = CreateMeasurementMapService(new MatrixAbnormalDetectionOptions(
            HighLimit: null,
            LowLimit: null,
            ZScoreThreshold: 100,
            LocalStdDevMultiplier: 100,
            LocalRelativeThreshold: 100));
        service.Update(CreateFrame(new double[,]
        {
            { 500, 600 },
            { 700, 1000 }
        }, timestamp));

        var analysis = service.GetLatestAnalysis();
        Assert.NotNull(analysis);

        var detail = service.BuildMeasurementMapSnapshot(
            analysis,
            new MatrixDisplayOptionsDto(
                MatrixScaleMode.FixedEngineeringRange,
                FixedMin: 0,
                FixedMax: 2000));
        var preview = service.BuildMatrixPreview(analysis);

        Assert.Equal(timestamp, detail.Timestamp);
        Assert.Equal(timestamp, preview.Timestamp);
        Assert.Same(analysis.Frame, detail.Frame);
        Assert.Same(analysis.Statistics, analysis.Frame.Statistics);
        Assert.Same(analysis.Statistics, detail.Statistics);
        Assert.Same(analysis.AbnormalPoints, detail.AbnormalPoints);
        Assert.Equal(analysis.QualityState, detail.QualityState);
        Assert.Equal(analysis.QualityState, preview.QualityState);
        Assert.Equal(analysis.Statistics.MaxValue, preview.Maximum);
        Assert.Equal(analysis.Statistics.AverageValue, preview.Average);
        Assert.Equal(analysis.Statistics.UniformityMinMax, preview.Uniformity);
        Assert.Equal(analysis.AbnormalPoints.Count, preview.AbnormalCount);
        Assert.Equal(4, detail.Cells.Count);
        Assert.Equal(4, preview.Cells.Count);

        var detailMaximum = detail.Cells.Single(cell => cell.Row == 1 && cell.Column == 1);
        var previewMaximum = preview.Cells.Single(cell => cell.Row == 1 && cell.Column == 1);
        Assert.Equal(0.5, detailMaximum.NormalizedValue);
        Assert.Equal(1, previewMaximum.NormalizedValue);
        Assert.Equal("Light Intensity", preview.MatrixType);
        Assert.Equal("lux", preview.Unit);
    }

    [Fact]
    public void MatrixPreview_MapsAllSixteenBySixteenCellsInRowMajorOrder()
    {
        var values = new double[16, 16];
        for (var row = 0; row < 16; row++)
        {
            for (var column = 0; column < 16; column++)
            {
                values[row, column] = 500 + row * 16 + column;
            }
        }

        var service = CreateMeasurementMapService(new MatrixAbnormalDetectionOptions(
            HighLimit: null,
            LowLimit: null,
            ZScoreThreshold: 100,
            LocalStdDevMultiplier: 100,
            LocalRelativeThreshold: 100));
        service.Update(CreateFrame(values));

        var analysis = service.GetLatestAnalysis();
        Assert.NotNull(analysis);
        var preview = service.BuildMatrixPreview(analysis);

        Assert.Equal(16, preview.Rows);
        Assert.Equal(16, preview.Columns);
        Assert.Equal(256, preview.Cells.Count);
        Assert.Equal((0, 0), (preview.Cells[0].Row, preview.Cells[0].Column));
        Assert.Equal((15, 15), (preview.Cells[^1].Row, preview.Cells[^1].Column));
        Assert.Equal(0, preview.Cells[0].NormalizedValue);
        Assert.Equal(1, preview.Cells[^1].NormalizedValue);
    }

    [Fact]
    public void MatrixPreview_MapsInvalidCellAndSelectsInvalidValueAsMainAlarm()
    {
        var service = new MeasurementMapService(new MatrixFrameCache());
        service.Update(CreateFrame(new double[,]
        {
            { 500, 2000 },
            { 50, double.NaN }
        }));

        var analysis = service.GetLatestAnalysis();
        Assert.NotNull(analysis);
        var preview = service.BuildMatrixPreview(analysis);

        var invalidCell = preview.Cells.Single(cell => cell.Row == 1 && cell.Column == 1);
        Assert.False(invalidCell.IsValid);
        Assert.True(invalidCell.IsAbnormal);
        Assert.Equal(MatrixSeverity.Alarm, invalidCell.Severity);
        Assert.Equal(new RgbColorDto(120, 120, 120), invalidCell.Color);

        Assert.NotNull(preview.MainAbnormalPoint);
        Assert.Equal((1, 1), (preview.MainAbnormalPoint.Row, preview.MainAbnormalPoint.Column));
        Assert.Equal(MatrixAbnormalType.InvalidValue, preview.MainAbnormalPoint.Type);
        Assert.Equal(MatrixSeverity.Alarm, preview.MainAbnormalPoint.Severity);
    }

    [Fact]
    public void MatrixPreview_MainAbnormalPointUsesStableCoordinateTieBreaker()
    {
        var statistics = new MatrixStatisticsDto(100, 900, 500, 100, 0.2, 0.2, 4, 0);
        var frame = new MatrixFrameDto(
            DateTime.UtcNow,
            2,
            2,
            new double[,] { { 100, 900 }, { 900, 100 } },
            statistics);
        var abnormalPoints = new AbnormalMatrixPointDto[]
        {
            new(1, 0, 900, MatrixAbnormalType.HighLimit, MatrixSeverity.Alarm, "high"),
            new(0, 1, 900, MatrixAbnormalType.HighLimit, MatrixSeverity.Alarm, "high")
        };
        var analysis = new MatrixAnalysisSnapshotDto(
            frame.Timestamp,
            frame,
            statistics,
            abnormalPoints,
            MatrixQualityState.Alarm);
        var service = new MeasurementMapService(new MatrixFrameCache());

        var preview = service.BuildMatrixPreview(analysis);

        Assert.NotNull(preview.MainAbnormalPoint);
        Assert.Equal((0, 1), (preview.MainAbnormalPoint.Row, preview.MainAbnormalPoint.Column));
    }

    [Fact]
    public void MeasurementMapService_ReturnsNullAnalysisWhenNoFrameExists()
    {
        var service = new MeasurementMapService(new MatrixFrameCache());

        Assert.Null(service.GetLatestAnalysis());
        Assert.Null(service.GetLatestSnapshot());
    }

    [Fact]
    public void DataCleanPipelineAndSnapshot_UseTheSameAbnormalCount()
    {
        var timestamp = DateTime.UtcNow;
        var matrix = CreateFrame(new double[,]
        {
            { 500, 2000 },
            { 50, double.NaN }
        }, timestamp);
        var rawFrame = new RawMeasurementFrame(
            Guid.NewGuid(),
            "MCMD-001",
            1,
            timestamp,
            DeviceStatus.Running,
            [],
            matrix,
            0,
            TagQuality.Good);
        var pipeline = new DataCleanPipeline(TagDefinitionCatalog.CreateDefaults());
        var service = new MeasurementMapService(new MatrixFrameCache());
        service.Update(matrix);

        var tags = pipeline.CleanToCleanedValues(rawFrame);
        var snapshot = service.GetLatestSnapshot();

        var abnormalCount = tags.Single(tag => tag.TagId == "MATRIX.LIGHT.ABNORMAL_COUNT").NumericValue;
        Assert.NotNull(snapshot);
        Assert.Equal(snapshot.AbnormalPoints.Count, abnormalCount);
    }

    [Theory]
    [InlineData(0.85, MatrixQualityState.Good)]
    [InlineData(0.75, MatrixQualityState.Attention)]
    [InlineData(0.65, MatrixQualityState.Warning)]
    public void MatrixQualityEvaluator_UsesUniformityThresholds(
        double uniformity,
        MatrixQualityState expectedState)
    {
        var statistics = new MatrixStatisticsDto(1, 2, 1.5, 0.1, uniformity, uniformity, 4, 0);

        var state = new MatrixQualityEvaluator().Evaluate(statistics, []);

        Assert.Equal(expectedState, state);
    }

    [Fact]
    public void MatrixQualityEvaluator_AlarmTakesPriority()
    {
        var statistics = new MatrixStatisticsDto(1, 1, 1, 0, 1, 1, 3, 1);
        var warningPoint = new AbnormalMatrixPointDto(
            0, 0, 1, MatrixAbnormalType.LocalHotspot, MatrixSeverity.Warning, "warning");

        var state = new MatrixQualityEvaluator().Evaluate(statistics, [warningPoint]);

        Assert.Equal(MatrixQualityState.Alarm, state);
    }

    private static MatrixFrame CreateFrame(double[,] values, DateTime? timestamp = null) => new(
        Guid.NewGuid(),
        timestamp ?? DateTime.UtcNow,
        values.GetLength(0),
        values.GetLength(1),
        values);

    private static MatrixStatisticsDto CreateStatistics(double min, double max) => new(
        min,
        max,
        (min + max) / 2,
        1,
        min / max,
        min / ((min + max) / 2),
        2,
        0);

    private static MeasurementMapService CreateMeasurementMapService(MatrixAbnormalDetectionOptions options) => new(
        new MatrixFrameCache(),
        new AbnormalPointDetector(),
        new MatrixScaleService(),
        new IndustrialHeatColorMapService(),
        new MatrixQualityEvaluator(),
        options);

    private static AbnormalMatrixPointDto Find(
        IReadOnlyList<AbnormalMatrixPointDto> points,
        int row,
        int column) => points.Single(point => point.Row == row && point.Column == column);
}
