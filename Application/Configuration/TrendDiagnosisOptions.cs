namespace Application.Configuration;

public sealed record TrendDiagnosisOptions
{
    public int SpikeLookback { get; init; } = 15;
    public double SpikeMadMultiplier { get; init; } = 6;
    public double MinimumSpikePercentOfSpan { get; init; } = 1;
    public int DriftMinimumPoints { get; init; } = 20;
    public TimeSpan DriftWindow { get; init; } = TimeSpan.FromMinutes(1);
    public double DriftThresholdPercentOfSpanPerMinute { get; init; } = 1;

    public void Validate()
    {
        if (SpikeLookback < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SpikeLookback),
                "Spike lookback must be at least 3 points.");
        }

        if (!double.IsFinite(SpikeMadMultiplier) || SpikeMadMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SpikeMadMultiplier),
                "Spike MAD multiplier must be a positive finite number.");
        }

        if (!double.IsFinite(MinimumSpikePercentOfSpan)
            || MinimumSpikePercentOfSpan <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumSpikePercentOfSpan),
                "Minimum spike percent of span must be a positive finite number.");
        }

        if (DriftMinimumPoints < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DriftMinimumPoints),
                "Drift minimum points must be at least 2.");
        }

        if (DriftWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DriftWindow),
                "Drift window must be greater than zero.");
        }

        if (!double.IsFinite(DriftThresholdPercentOfSpanPerMinute)
            || DriftThresholdPercentOfSpanPerMinute <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DriftThresholdPercentOfSpanPerMinute),
                "Drift threshold must be a positive finite number.");
        }
    }
}
