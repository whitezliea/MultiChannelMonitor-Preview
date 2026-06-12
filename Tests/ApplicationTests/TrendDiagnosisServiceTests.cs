using Application.Configuration;
using Application.DTOs.Charts;
using Application.Services.Trend;
using Domain.Tags;

namespace Tests.ApplicationTests;

public class TrendDiagnosisServiceTests
{
    private static readonly DateTime Start =
        new(2026, 6, 12, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Analyze_ReturnsStableForConstantGoodSeries()
    {
        var service = CreateService();
        var points = Enumerable.Range(0, 20)
            .Select(index => Point(index, 50))
            .ToArray();

        var result = service.Analyze(points, 0, 100);

        Assert.Equal(TrendDiagnosisState.Stable, result.Diagnosis.State);
        Assert.False(result.Diagnosis.HasSpike);
        Assert.False(result.Diagnosis.HasDrift);
        Assert.Equal(0, result.Diagnosis.SpikeCount);
        Assert.All(result.Points, point => Assert.False(point.IsSpike));
    }

    [Fact]
    public void Analyze_MarksSpikeUsingPriorMedianAndMad()
    {
        var service = CreateService();
        var points = Enumerable.Range(0, 8)
            .Select(index => Point(index, index == 6 ? 30 : 10))
            .ToArray();

        var result = service.Analyze(points, 0, 100);

        Assert.Equal(TrendDiagnosisState.Spike, result.Diagnosis.State);
        Assert.True(result.Diagnosis.HasSpike);
        Assert.Equal(1, result.Diagnosis.SpikeCount);
        Assert.True(result.Points[6].IsSpike);
        Assert.False(result.Points[7].IsSpike);
    }

    [Fact]
    public void Analyze_DetectsNormalizedRisingDrift()
    {
        var service = CreateService(
            new TrendDiagnosisOptions
            {
                SpikeLookback = 5,
                SpikeMadMultiplier = 100,
                MinimumSpikePercentOfSpan = 1,
                DriftMinimumPoints = 10,
                DriftWindow = TimeSpan.FromMinutes(1),
                DriftThresholdPercentOfSpanPerMinute = 1
            });
        var points = Enumerable.Range(0, 20)
            .Select(index => new TrendPointDto(
                Start.AddSeconds(index * 3),
                index * 5d / 19d))
            .ToArray();

        var result = service.Analyze(points, 0, 100);

        Assert.Equal(TrendDiagnosisState.Drift, result.Diagnosis.State);
        Assert.False(result.Diagnosis.HasSpike);
        Assert.True(result.Diagnosis.HasDrift);
        Assert.NotNull(result.Diagnosis.SlopePerMinute);
        Assert.True(result.Diagnosis.NormalizedSlopePercentPerMinute > 5);
        Assert.Contains("rising", result.Diagnosis.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_ExcludesNonGoodPointsFromSpikeAndDrift()
    {
        var service = CreateService();
        var points = Enumerable.Range(0, 20)
            .Select(index => Point(
                index,
                index == 10 ? 1000 : 10,
                index == 10 ? TagQuality.Offline : TagQuality.Good))
            .ToArray();

        var result = service.Analyze(points, 0, 100);

        Assert.False(result.Points[10].IsSpike);
        Assert.Equal(TagQuality.Offline, result.Points[10].Quality);
        Assert.False(result.Diagnosis.HasSpike);
        Assert.False(result.Diagnosis.HasDrift);
    }

    [Fact]
    public void Analyze_DoesNotEvaluateDriftWithoutEngineeringRange()
    {
        var service = CreateService();
        var points = Enumerable.Range(0, 20)
            .Select(index => Point(index, index))
            .ToArray();

        var result = service.Analyze(points, 0, null);

        Assert.Equal(TrendDiagnosisState.NotEvaluated, result.Diagnosis.State);
        Assert.False(result.Diagnosis.HasDrift);
        Assert.Null(result.Diagnosis.NormalizedSlopePercentPerMinute);
        Assert.Contains(
            "engineering range",
            result.Diagnosis.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        var options = new TrendDiagnosisOptions { SpikeLookback = 2 };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TrendDiagnosisService(options));
    }

    private static TrendDiagnosisService CreateService(
        TrendDiagnosisOptions? options = null) =>
        new(options ?? new TrendDiagnosisOptions
        {
            SpikeLookback = 5,
            SpikeMadMultiplier = 6,
            MinimumSpikePercentOfSpan = 1,
            DriftMinimumPoints = 5,
            DriftWindow = TimeSpan.FromMinutes(1),
            DriftThresholdPercentOfSpanPerMinute = 1
        });

    private static TrendPointDto Point(
        int index,
        double value,
        TagQuality quality = TagQuality.Good) =>
        new(Start.AddSeconds(index * 3), value, quality);
}
