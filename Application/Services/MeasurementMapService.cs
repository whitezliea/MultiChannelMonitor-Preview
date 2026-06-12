using Application.Caches;
using Application.DTOs.Dashboard;
using Application.DTOs.MeasurementMap;
using Application.Services.MeasurementMap;
using Domain.Measurements;

namespace Application.Services;

public sealed class MeasurementMapService
{
    private static readonly MatrixDisplayOptionsDto PreviewDisplayOptions = new(
        ScaleMode: MatrixScaleMode.AutoCurrentFrame,
        Palette: MatrixPalette.IndustrialHeat,
        MatrixType: "Light Intensity",
        Unit: "lux");

    private readonly MatrixFrameCache _cache;
    private readonly AbnormalPointDetector _abnormalPointDetector;
    private readonly MatrixScaleService _scaleService;
    private readonly MatrixHeatmapMapper _heatmapMapper;
    private readonly MatrixQualityEvaluator _qualityEvaluator;
    private readonly MatrixAbnormalDetectionOptions _detectionOptions;

    public MeasurementMapService(MatrixFrameCache cache)
        : this(
            cache,
            new AbnormalPointDetector(),
            new MatrixScaleService(),
            new IndustrialHeatColorMapService(),
            new MatrixQualityEvaluator(),
            MatrixAbnormalDetectionOptions.Default)
    {
    }

    public MeasurementMapService(
        MatrixFrameCache cache,
        AbnormalPointDetector abnormalPointDetector,
        MatrixScaleService scaleService,
        IndustrialHeatColorMapService colorMapService,
        MatrixQualityEvaluator qualityEvaluator,
        MatrixAbnormalDetectionOptions detectionOptions)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _abnormalPointDetector = abnormalPointDetector ?? throw new ArgumentNullException(nameof(abnormalPointDetector));
        _scaleService = scaleService ?? throw new ArgumentNullException(nameof(scaleService));
        _heatmapMapper = new MatrixHeatmapMapper(
            scaleService,
            colorMapService ?? throw new ArgumentNullException(nameof(colorMapService)));
        _qualityEvaluator = qualityEvaluator ?? throw new ArgumentNullException(nameof(qualityEvaluator));
        _detectionOptions = detectionOptions ?? throw new ArgumentNullException(nameof(detectionOptions));
    }

    public void Update(MatrixFrame frame) => _cache.Update(frame);

    public MatrixFrameDto? GetLatest()
    {
        var frame = _cache.GetLatest();
        if (frame is null)
        {
            return null;
        }

        return MapFrame(frame);
    }

    public MeasurementMapSnapshotDto? GetLatestSnapshot(MatrixDisplayOptionsDto? options = null)
    {
        var analysis = GetLatestAnalysis();
        if (analysis is null)
        {
            return null;
        }

        return BuildMeasurementMapSnapshot(analysis, options);
    }

    public MatrixAnalysisSnapshotDto? GetLatestAnalysis()
    {
        var frame = _cache.GetLatest();
        if (frame is null)
        {
            return null;
        }

        return Analyze(frame);
    }

    public MatrixAnalysisSnapshotDto Analyze(MatrixFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var statistics = frame.CalculateStatistics();
        var frameDto = MapFrame(frame, statistics);
        var abnormalPoints = _abnormalPointDetector.Detect(frame, statistics, _detectionOptions);
        var qualityState = _qualityEvaluator.Evaluate(frameDto.Statistics, abnormalPoints);

        return new MatrixAnalysisSnapshotDto(
            frame.Timestamp,
            frameDto,
            frameDto.Statistics,
            abnormalPoints,
            qualityState,
            frame.SourceFrameId,
            frame.SequenceNo);
    }

    public MeasurementMapSnapshotDto BuildMeasurementMapSnapshot(
        MatrixAnalysisSnapshotDto analysis,
        MatrixDisplayOptionsDto? options = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        options ??= new MatrixDisplayOptionsDto();

        var scaleRange = _scaleService.Resolve(analysis.Statistics, options);
        var cells = _heatmapMapper.Map(
            analysis.Frame,
            scaleRange,
            analysis.AbnormalPoints,
            options.Unit);

        return new MeasurementMapSnapshotDto(
            analysis.Timestamp,
            options.MatrixType,
            options.Unit,
            analysis.Frame,
            scaleRange,
            analysis.Statistics,
            cells,
            analysis.AbnormalPoints,
            analysis.QualityState,
            analysis.SourceFrameId,
            analysis.SequenceNo);
    }

    public MatrixPreviewDto BuildMatrixPreview(MatrixAnalysisSnapshotDto analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var scaleRange = _scaleService.Resolve(analysis.Statistics, PreviewDisplayOptions);
        var heatmapCells = _heatmapMapper.Map(
            analysis.Frame,
            scaleRange,
            analysis.AbnormalPoints,
            PreviewDisplayOptions.Unit);
        var cells = heatmapCells
            .Select(cell => new MatrixPreviewCellDto(
                cell.Row,
                cell.Column,
                cell.NormalizedValue,
                cell.Color,
                cell.IsValid,
                cell.IsAbnormal,
                cell.Severity))
            .ToArray();
        var mainAbnormalPoint = SelectMainAbnormalPoint(analysis);

        return new MatrixPreviewDto(
            analysis.Timestamp,
            analysis.Frame.Rows,
            analysis.Frame.Columns,
            PreviewDisplayOptions.MatrixType,
            PreviewDisplayOptions.Unit,
            analysis.QualityState,
            analysis.Statistics.MaxValue,
            analysis.Statistics.AverageValue,
            analysis.Statistics.UniformityMinMax,
            analysis.AbnormalPoints.Count,
            mainAbnormalPoint,
            cells,
            analysis.SourceFrameId,
            analysis.SequenceNo);
    }

    private static MatrixPreviewPointDto? SelectMainAbnormalPoint(MatrixAnalysisSnapshotDto analysis)
    {
        var point = analysis.AbnormalPoints
            .OrderByDescending(item => GetSeverityPriority(item.Severity))
            .ThenByDescending(item => GetTypePriority(item.Type))
            .ThenByDescending(item => GetDeviationFromAverage(item.Value, analysis.Statistics.AverageValue))
            .ThenBy(item => item.Row)
            .ThenBy(item => item.Column)
            .FirstOrDefault();

        return point is null
            ? null
            : new MatrixPreviewPointDto(
                point.Row,
                point.Column,
                point.Value,
                point.Type,
                point.Severity);
    }

    private static int GetSeverityPriority(MatrixSeverity severity) => severity switch
    {
        MatrixSeverity.Alarm => 3,
        MatrixSeverity.Warning => 2,
        MatrixSeverity.Info => 1,
        _ => 0
    };

    private static int GetTypePriority(MatrixAbnormalType type) => type switch
    {
        MatrixAbnormalType.InvalidValue => 4,
        MatrixAbnormalType.HighLimit or MatrixAbnormalType.LowLimit => 3,
        MatrixAbnormalType.StatisticalHotspot or MatrixAbnormalType.StatisticalColdspot => 2,
        MatrixAbnormalType.LocalHotspot or MatrixAbnormalType.LocalColdspot => 1,
        _ => 0
    };

    private static double GetDeviationFromAverage(double value, double average) =>
        double.IsFinite(value) && double.IsFinite(average)
            ? Math.Abs(value - average)
            : 0d;

    private static MatrixFrameDto MapFrame(MatrixFrame frame) => MapFrame(frame, frame.CalculateStatistics());

    private static MatrixFrameDto MapFrame(MatrixFrame frame, MatrixStatistics statistics) =>
        new(
            frame.Timestamp,
            frame.Rows,
            frame.Columns,
            frame.Values,
            new MatrixStatisticsDto(
                statistics.MinValue,
                statistics.MaxValue,
                statistics.AverageValue,
                statistics.StdDev,
                statistics.UniformityMinMax,
                statistics.UniformityMinAverage,
                statistics.ValidCount,
                statistics.InvalidCount),
            frame.SourceFrameId,
            frame.SequenceNo);
}
