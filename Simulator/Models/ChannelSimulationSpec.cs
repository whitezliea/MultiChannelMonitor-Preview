namespace Simulator.Models;

public sealed record ChannelSimulationSpec(
    string Code,
    string Unit,
    double BaseValue,
    double PhysicalMin,
    double PhysicalMax,
    double NoiseSigma,
    double SineAmplitude,
    double SinePeriodSeconds,
    double DriftPerSecond,
    double DriftMin,
    double DriftMax);
