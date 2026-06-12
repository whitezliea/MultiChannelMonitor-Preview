namespace Simulator.Noise;

public sealed class RandomNoise
{
    private readonly Random _random;

    public RandomNoise(int seed)
    {
        _random = new Random(seed);
    }

    public double Next(double amplitude) => (_random.NextDouble() * 2 - 1) * amplitude;
}
