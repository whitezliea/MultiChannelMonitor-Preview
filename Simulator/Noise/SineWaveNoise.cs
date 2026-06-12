namespace Simulator.Noise;

public static class SineWaveNoise
{
    public static double Value(double phase, double amplitude) => Math.Sin(phase) * amplitude;

    public static double Calculate(double elapsedSeconds, double amplitude, double periodSeconds, double phase = 0)
    {
        if (periodSeconds <= 0 || amplitude == 0)
        {
            return 0;
        }

        var angle = 2.0 * Math.PI * elapsedSeconds / periodSeconds + phase;
        return amplitude * Math.Sin(angle);
    }
}
