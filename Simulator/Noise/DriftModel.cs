namespace Simulator.Noise;

public static class DriftModel
{
    public static double Advance(double current, double driftPerSecond, double deltaSeconds, double min, double max) =>
        Math.Clamp(current + driftPerSecond * deltaSeconds, min, max);
}
