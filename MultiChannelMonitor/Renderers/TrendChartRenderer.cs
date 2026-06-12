using Application.DTOs.Charts;
using ScottPlot;
using ScottPlot.WPF;

namespace Presentation.Wpf.Renderers;

public sealed class TrendChartRenderer
{
    private static readonly Color TrendColor = Color.FromHex("#2563EB");
    private static readonly Color WarningColor = Color.FromHex("#F79009");
    private static readonly Color AlarmColor = Color.FromHex("#B42318");
    private static readonly Color CurrentPointColor = Color.FromHex("#17202A");
    private static readonly Color BadQualityColor = Color.FromHex("#667085");
    private static readonly Color SpikeColor = Color.FromHex("#D92D20");
    private RenderKey? _lastRenderKey;
    private bool _isCleared = true;

    public bool Render(WpfPlot plotControl, TrendSnapshotDto snapshot)
    {
        ArgumentNullException.ThrowIfNull(plotControl);
        ArgumentNullException.ThrowIfNull(snapshot);

        var renderKey = RenderKey.FromSnapshot(snapshot);
        if (!_isCleared && renderKey == _lastRenderKey)
        {
            return false;
        }

        var plot = plotControl.Plot;
        plot.Clear();
        plot.Title($"{snapshot.Metadata.DisplayName} Trend - {snapshot.TagId}");
        plot.XLabel("Time");
        plot.YLabel(string.IsNullOrWhiteSpace(snapshot.Metadata.Unit)
            ? "Value"
            : snapshot.Metadata.Unit);
        plot.Axes.DateTimeTicksBottom();

        var finitePoints = snapshot.Series.Points
            .Where(point => double.IsFinite(point.Value))
            .ToArray();
        AddTrendLine(plot, finitePoints, snapshot.Metadata.DisplayName);

        AddQualityMarkers(plot, finitePoints);
        AddSpikeMarkers(plot, finitePoints);

        foreach (var threshold in snapshot.Thresholds)
        {
            var line = plot.Add.HorizontalLine(threshold.Value);
            line.Color = IsAlarm(threshold.Type) ? AlarmColor : WarningColor;
            line.LineWidth = 1.5f;
            line.LinePattern = LinePattern.Dashed;
            line.LegendText = $"{threshold.Name} ({threshold.Value:0.###})";
        }

        if (snapshot.CurrentValue is { } currentValue
            && double.IsFinite(currentValue)
            && snapshot.CurrentTimestamp is { } currentTimestamp)
        {
            var currentPoint = plot.Add.Marker(
                currentTimestamp.ToLocalTime().ToOADate(),
                currentValue);
            currentPoint.Color = CurrentPointColor;
            currentPoint.Size = 9;
            currentPoint.Shape = MarkerShape.FilledCircle;
            currentPoint.LegendText = "Current";
        }

        ApplyAxisLimits(plot, snapshot, finitePoints);
        plot.ShowLegend(Alignment.UpperRight);
        plotControl.Refresh();
        _lastRenderKey = renderKey;
        _isCleared = false;
        return true;
    }

    public bool RenderPreview(WpfPlot plotControl, TrendSeriesDto series)
    {
        ArgumentNullException.ThrowIfNull(plotControl);
        ArgumentNullException.ThrowIfNull(series);

        var renderKey = RenderKey.FromSeries(series);
        if (!_isCleared && renderKey == _lastRenderKey)
        {
            return false;
        }

        var plot = plotControl.Plot;
        plot.Clear();
        plot.XLabel("Time");
        plot.Axes.DateTimeTicksBottom();

        var finitePoints = series.Points
            .Where(point => double.IsFinite(point.Value))
            .ToArray();
        AddTrendLine(plot, finitePoints, series.TagId);
        AddQualityMarkers(plot, finitePoints);
        ApplyPreviewAxisLimits(plot, finitePoints);

        plotControl.Refresh();
        _lastRenderKey = renderKey;
        _isCleared = false;
        return true;
    }

    public bool Clear(WpfPlot plotControl)
    {
        ArgumentNullException.ThrowIfNull(plotControl);
        if (_isCleared)
        {
            return false;
        }

        plotControl.Plot.Clear();
        plotControl.Refresh();
        _lastRenderKey = null;
        _isCleared = true;
        return true;
    }

    public void Invalidate() =>
        _lastRenderKey = null;

    private static void ApplyAxisLimits(
        Plot plot,
        TrendSnapshotDto snapshot,
        IReadOnlyList<TrendPointDto> finitePoints)
    {
        var localWindowEnd = snapshot.CapturedAt.ToLocalTime();
        var localWindowStart = (snapshot.CapturedAt - snapshot.Window).ToLocalTime();
        plot.Axes.SetLimitsX(localWindowStart.ToOADate(), localWindowEnd.ToOADate());

        var values = finitePoints
            .Select(point => point.Value)
            .Concat(snapshot.Thresholds.Select(threshold => threshold.Value))
            .Where(double.IsFinite);
        ApplyValueAxisLimits(plot, values);
    }

    private static void ApplyPreviewAxisLimits(
        Plot plot,
        IReadOnlyList<TrendPointDto> points)
    {
        if (points.Count == 0)
        {
            plot.Axes.SetLimits(0, 1, 0, 1);
            return;
        }

        var firstTimestamp = points[0].Timestamp.ToLocalTime();
        var lastTimestamp = points[^1].Timestamp.ToLocalTime();
        if (firstTimestamp == lastTimestamp)
        {
            firstTimestamp = firstTimestamp.AddSeconds(-1);
            lastTimestamp = lastTimestamp.AddSeconds(1);
        }

        plot.Axes.SetLimitsX(firstTimestamp.ToOADate(), lastTimestamp.ToOADate());
        ApplyValueAxisLimits(plot, points.Select(point => point.Value));
    }

    private static void ApplyValueAxisLimits(Plot plot, IEnumerable<double> sourceValues)
    {
        var values = sourceValues.Where(double.IsFinite).ToArray();
        if (values.Length == 0)
        {
            plot.Axes.SetLimitsY(0, 1);
            return;
        }

        var minimum = values.Min();
        var maximum = values.Max();
        if (Math.Abs(maximum - minimum) < 1e-9)
        {
            var halfSpan = Math.Max(Math.Abs(minimum) * 0.1, 1d);
            plot.Axes.SetLimitsY(minimum - halfSpan, maximum + halfSpan);
            return;
        }

        var padding = (maximum - minimum) * 0.1;
        plot.Axes.SetLimitsY(minimum - padding, maximum + padding);
    }

    private static bool IsAlarm(TrendThresholdType type) =>
        type is TrendThresholdType.AlarmLow or TrendThresholdType.AlarmHigh;

    private static void AddTrendLine(
        Plot plot,
        IReadOnlyList<TrendPointDto> points,
        string legendText)
    {
        if (points.Count == 0)
        {
            return;
        }

        var trend = plot.Add.Scatter(
            points.Select(point => point.Timestamp.ToLocalTime().ToOADate()).ToArray(),
            points.Select(point => point.Value).ToArray());
        trend.Color = TrendColor;
        trend.LineWidth = 2;
        trend.MarkerSize = 0;
        trend.LegendText = legendText;
    }

    private static void AddQualityMarkers(
        Plot plot,
        IReadOnlyList<TrendPointDto> points)
    {
        var qualityPoints = points
            .Where(point => point.Quality != Domain.Tags.TagQuality.Good)
            .ToArray();
        if (qualityPoints.Length == 0)
        {
            return;
        }

        var markers = plot.Add.Scatter(
            qualityPoints
                .Select(point => point.Timestamp.ToLocalTime().ToOADate())
                .ToArray(),
            qualityPoints.Select(point => point.Value).ToArray());
        markers.LineWidth = 0;
        markers.MarkerSize = 8;
        markers.MarkerShape = MarkerShape.Cross;
        markers.Color = BadQualityColor;
        markers.LegendText = "Non-Good Quality";
    }

    private static void AddSpikeMarkers(
        Plot plot,
        IReadOnlyList<TrendPointDto> points)
    {
        var spikePoints = points
            .Where(point => point.IsSpike)
            .ToArray();
        if (spikePoints.Length == 0)
        {
            return;
        }

        var markers = plot.Add.Scatter(
            spikePoints
                .Select(point => point.Timestamp.ToLocalTime().ToOADate())
                .ToArray(),
            spikePoints.Select(point => point.Value).ToArray());
        markers.LineWidth = 0;
        markers.MarkerSize = 10;
        markers.MarkerShape = MarkerShape.FilledTriangleUp;
        markers.Color = SpikeColor;
        markers.LegendText = "Spike";
    }

    private sealed record RenderKey(
        string TagId,
        long WindowTicks,
        long ConfigurationRevision,
        long SequenceNo,
        int PointCount,
        DateTime? FirstPointTimestamp,
        DateTime? LastPointTimestamp,
        double? CurrentValue,
        Domain.Tags.TagQuality? CurrentQuality,
        Domain.Tags.TagAlarmState? CurrentAlarmState,
        TrendDiagnosisState DiagnosisState,
        int SpikeCount)
    {
        public static RenderKey FromSnapshot(TrendSnapshotDto snapshot) =>
            new(
                snapshot.TagId,
                snapshot.Window.Ticks,
                snapshot.ConfigurationRevision,
                snapshot.SequenceNo,
                snapshot.Series.Points.Count,
                snapshot.Series.Points.FirstOrDefault()?.Timestamp,
                snapshot.Series.Points.LastOrDefault()?.Timestamp,
                snapshot.CurrentValue,
                snapshot.CurrentQuality,
                snapshot.CurrentAlarmState,
                snapshot.Diagnosis.State,
                snapshot.Diagnosis.SpikeCount);

        public static RenderKey FromSeries(TrendSeriesDto series) =>
            new(
                series.TagId,
                0,
                0,
                series.SequenceNo,
                series.Points.Count,
                series.Points.FirstOrDefault()?.Timestamp,
                series.Points.LastOrDefault()?.Timestamp,
                series.Points.LastOrDefault()?.Value,
                series.Points.LastOrDefault()?.Quality,
                null,
                TrendDiagnosisState.NotEvaluated,
                0);
    }
}
