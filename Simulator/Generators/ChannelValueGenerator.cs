using Domain.Measurements;
using Domain.Tags;
using Simulator.Models;
using Simulator.Noise;
using Simulator.Scenarios;

namespace Simulator.Generators;

public sealed class ChannelValueGenerator
{
    private readonly Random _random;
    private readonly Dictionary<string, ChannelSimulationState> _states = [];

    public ChannelValueGenerator(int seed = 20260607)
    {
        _random = new Random(seed);
    }

    public ChannelValue Generate(ChannelSimulationSpec spec, TimeSpan elapsed, double deltaSeconds, ChannelEffect effect)
    {
        var state = GetOrCreateState(spec.Code);
        state.AdvanceDrift(spec.DriftPerSecond, deltaSeconds, spec.DriftMin, spec.DriftMax);

        if (effect.MissingValue)
        {
            return new ChannelValue(
                spec.Code,
                double.NaN,
                spec.Unit,
                effect.ForcedQuality ?? TagQuality.Bad,
                effect.ErrorCode);
        }

        var periodic = SineWaveNoise.Calculate(elapsed.TotalSeconds, spec.SineAmplitude, spec.SinePeriodSeconds, state.Phase);
        var noise = _random.NextGaussian(0, spec.NoiseSigma);
        var value = spec.BaseValue + periodic + state.Drift + noise;
        value = value * effect.Scale + effect.Offset;

        if (effect.OverrideValue.HasValue)
        {
            value = effect.OverrideValue.Value;
        }

        var quality = effect.ForcedQuality ?? EvaluateQuality(value, spec);
        return new ChannelValue(spec.Code, Math.Round(value, 4), spec.Unit, quality, effect.ErrorCode);
    }

    private ChannelSimulationState GetOrCreateState(string code)
    {
        if (_states.TryGetValue(code, out var state))
        {
            return state;
        }

        state = new ChannelSimulationState(_random.NextDouble() * 2.0 * Math.PI);
        _states[code] = state;
        return state;
    }

    private static TagQuality EvaluateQuality(double value, ChannelSimulationSpec spec)
    {
        if (value < spec.PhysicalMin || value > spec.PhysicalMax)
        {
            return TagQuality.OutOfRange;
        }

        return TagQuality.Good;
    }
}
