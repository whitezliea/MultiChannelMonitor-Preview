namespace Simulator.Noise;

public static class RandomExtensions
{
    public static double NextGaussian(this Random random, double mean = 0.0, double stdDev = 1.0)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        var radius = Math.Sqrt(-2.0 * Math.Log(u1));
        var theta = 2.0 * Math.PI * u2;
        return mean + stdDev * radius * Math.Cos(theta);
    }
}
