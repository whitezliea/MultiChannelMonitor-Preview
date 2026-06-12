using Application.Configuration;
using Application.DTOs.Charts;
using Domain.Tags;

namespace Application.Services.Trend;

public sealed record TrendDiagnosisResult(
    IReadOnlyList<TrendPointDto> Points,
    TrendDiagnosisDto Diagnosis);

public sealed class TrendDiagnosisService
{
    private const double MadConsistencyFactor = 1.4826;
    private readonly TrendDiagnosisOptions _options;

    public TrendDiagnosisService(TrendDiagnosisOptions? options = null)
    {
        _options = options ?? new TrendDiagnosisOptions();
        _options.Validate();
    }

    public TrendDiagnosisResult Analyze(
        IReadOnlyList<TrendPointDto> points,
        double? engineeringMinimum,
        double? engineeringMaximum)
    {
        ArgumentNullException.ThrowIfNull(points);

        var orderedPoints = points
            .OrderBy(point => point.Timestamp)
            .ToArray();
        var validPoints = orderedPoints
            .Where(IsValidForDiagnosis)
            .ToArray();
        if (validPoints.Length < Math.Max(_options.SpikeLookback + 1, _options.DriftMinimumPoints))
        {
            return new TrendDiagnosisResult(
                orderedPoints,
                new TrendDiagnosisDto(
                    TrendDiagnosisState.InsufficientData,
                    false,
                    false,
                    "Not enough Good samples are available for trend diagnosis."));
        }

        var engineeringSpan = ResolveEngineeringSpan(
            engineeringMinimum,
            engineeringMaximum);
        var spikeReferenceSpan = engineeringSpan ?? ResolveObservedSpan(
            validPoints,
            validPoints.Min(point => point.Value));
        var minimumSpikeDeviation =
            spikeReferenceSpan * _options.MinimumSpikePercentOfSpan / 100d;
        var markedPoints = MarkSpikes(
            orderedPoints,
            minimumSpikeDeviation,
            out var spikeCount);
        var drift = engineeringSpan.HasValue
            ? EvaluateDrift(markedPoints, engineeringSpan.Value)
            : DriftResult.Insufficient;
        var hasSpike = spikeCount > 0;
        var state = hasSpike
            ? TrendDiagnosisState.Spike
            : !engineeringSpan.HasValue
                ? TrendDiagnosisState.NotEvaluated
                : drift.HasDrift
                ? TrendDiagnosisState.Drift
                : drift.SlopePerMinute.HasValue
                    ? TrendDiagnosisState.Stable
                    : TrendDiagnosisState.InsufficientData;

        return new TrendDiagnosisResult(
            markedPoints,
            new TrendDiagnosisDto(
                state,
                hasSpike,
                drift.HasDrift,
                BuildMessage(spikeCount, drift, engineeringSpan.HasValue),
                spikeCount,
                drift.SlopePerMinute,
                drift.NormalizedSlopePercentPerMinute));
    }

    private IReadOnlyList<TrendPointDto> MarkSpikes(
        IReadOnlyList<TrendPointDto> points,
        double minimumSpikeDeviation,
        out int spikeCount)
    {
        var acceptedValues = new List<double>(points.Count);
        var markedPoints = new TrendPointDto[points.Count];
        spikeCount = 0;

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (!IsValidForDiagnosis(point))
            {
                markedPoints[index] = point;
                continue;
            }

            var isSpike = false;
            if (acceptedValues.Count >= _options.SpikeLookback)
            {
                var baseline = acceptedValues
                    .TakeLast(_options.SpikeLookback)
                    .OrderBy(value => value)
                    .ToArray();
                var median = Median(baseline);
                var deviations = baseline
                    .Select(value => Math.Abs(value - median))
                    .OrderBy(value => value)
                    .ToArray();
                var scaledMad = Median(deviations) * MadConsistencyFactor;
                var threshold = Math.Max(
                    minimumSpikeDeviation,
                    _options.SpikeMadMultiplier * scaledMad);
                isSpike = Math.Abs(point.Value - median) >= threshold;
            }

            markedPoints[index] = point with { IsSpike = isSpike };
            if (isSpike)
            {
                spikeCount++;
            }
            else
            {
                acceptedValues.Add(point.Value);
            }
        }

        return markedPoints;
    }

    private DriftResult EvaluateDrift(
        IReadOnlyList<TrendPointDto> points,
        double referenceSpan)
    {
        var lastTimestamp = points
            .Where(IsValidForDiagnosis)
            .Select(point => (DateTime?)point.Timestamp)
            .LastOrDefault();
        if (!lastTimestamp.HasValue)
        {
            return DriftResult.Insufficient;
        }

        var from = lastTimestamp.Value - _options.DriftWindow;
        var driftPoints = points
            .Where(point => IsValidForDiagnosis(point)
                && !point.IsSpike
                && point.Timestamp >= from)
            .ToArray();
        if (driftPoints.Length < _options.DriftMinimumPoints)
        {
            return DriftResult.Insufficient;
        }

        var origin = driftPoints[0].Timestamp;
        var sumX = 0d;
        var sumY = 0d;
        var sumXX = 0d;
        var sumXY = 0d;
        foreach (var point in driftPoints)
        {
            var x = (point.Timestamp - origin).TotalMinutes;
            sumX += x;
            sumY += point.Value;
            sumXX += x * x;
            sumXY += x * point.Value;
        }

        var count = driftPoints.Length;
        var denominator = count * sumXX - sumX * sumX;
        if (Math.Abs(denominator) < 1e-12)
        {
            return DriftResult.Insufficient;
        }

        var slope = (count * sumXY - sumX * sumY) / denominator;
        var normalizedSlope = slope / referenceSpan * 100d;
        return new DriftResult(
            Math.Abs(normalizedSlope) >= _options.DriftThresholdPercentOfSpanPerMinute,
            slope,
            normalizedSlope,
            count);
    }

    private static double? ResolveEngineeringSpan(
        double? engineeringMinimum,
        double? engineeringMaximum)
    {
        if (engineeringMinimum.HasValue
            && engineeringMaximum.HasValue
            && double.IsFinite(engineeringMinimum.Value)
            && double.IsFinite(engineeringMaximum.Value)
            && engineeringMaximum.Value > engineeringMinimum.Value)
        {
            return engineeringMaximum.Value - engineeringMinimum.Value;
        }

        return null;
    }

    private static double ResolveObservedSpan(
        IReadOnlyList<TrendPointDto> validPoints,
        double observedMinimum)
    {
        var observedMaximum = validPoints.Max(point => point.Value);
        var observedSpan = observedMaximum - observedMinimum;
        if (observedSpan > 1e-12)
        {
            return observedSpan;
        }

        return Math.Max(Math.Abs(observedMinimum), 1d);
    }

    private static bool IsValidForDiagnosis(TrendPointDto point) =>
        point.Quality == TagQuality.Good && double.IsFinite(point.Value);

    private static double Median(IReadOnlyList<double> sortedValues)
    {
        var middle = sortedValues.Count / 2;
        return sortedValues.Count % 2 == 0
            ? (sortedValues[middle - 1] + sortedValues[middle]) / 2d
            : sortedValues[middle];
    }

    private static string BuildMessage(
        int spikeCount,
        DriftResult drift,
        bool hasEngineeringSpan)
    {
        if (spikeCount > 0 && drift.HasDrift)
        {
            return $"{spikeCount} spike(s) detected; drift is {FormatDirection(drift.NormalizedSlopePercentPerMinute)}.";
        }

        if (spikeCount > 0)
        {
            return hasEngineeringSpan
                ? $"{spikeCount} spike(s) detected in the selected window."
                : $"{spikeCount} spike(s) detected; drift requires a finite engineering range.";
        }

        if (!hasEngineeringSpan)
        {
            return "No spike detected; drift requires a finite engineering range.";
        }

        if (drift.HasDrift)
        {
            return $"Drift is {FormatDirection(drift.NormalizedSlopePercentPerMinute)}.";
        }

        if (!drift.SlopePerMinute.HasValue)
        {
            return "No spike detected; more samples are required for drift evaluation.";
        }

        return "No spike or significant drift detected.";
    }

    private static string FormatDirection(double? normalizedSlope)
    {
        if (!normalizedSlope.HasValue)
        {
            return "not available";
        }

        var direction = normalizedSlope.Value >= 0 ? "rising" : "falling";
        return $"{direction} at {Math.Abs(normalizedSlope.Value):0.###}% span/min";
    }

    private sealed record DriftResult(
        bool HasDrift,
        double? SlopePerMinute,
        double? NormalizedSlopePercentPerMinute,
        int PointCount)
    {
        public static DriftResult Insufficient { get; } =
            new(false, null, null, 0);
    }
}
