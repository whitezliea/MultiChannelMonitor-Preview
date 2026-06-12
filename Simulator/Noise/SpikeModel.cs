namespace Simulator.Noise;

public static class SpikeModel
{
    public static bool IsInsideWindow(TimeSpan elapsed, double startSecond, double endSecond, double cycleSeconds)
    {
        var t = elapsed.TotalSeconds % cycleSeconds;
        return t >= startSecond && t < endSecond;
    }
}
