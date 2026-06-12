namespace Simulator.Models;

public sealed record MatrixSimulationSpec(
    int Rows,
    int Columns,
    string ValueType,
    string Unit,
    double BaseValue,
    double CenterAmplitude,
    double EdgeDrop,
    double NoiseSigma);
