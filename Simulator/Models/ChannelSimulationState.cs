namespace Simulator.Models;

public sealed class ChannelSimulationState
{
    public ChannelSimulationState(double phase)
    {
        Phase = phase;
    }

    public double Drift { get; private set; }
    public double Phase { get; }

    public void AdvanceDrift(double driftPerSecond, double deltaSeconds, double min, double max)
    {
        Drift += driftPerSecond * deltaSeconds;
        Drift = Math.Clamp(Drift, min, max);
    }
}
