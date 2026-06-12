using Simulator.Models;

namespace Simulator.Profiles;

public static class DefaultInstrumentProfile
{
    public static IReadOnlyList<ChannelSimulationSpec> CreateChannels() =>
    [
        new("TEMP_CH01", "C", 25.0, -20.0, 120.0, 0.08, 0.25, 60, 0.001, -1.5, 8.0),
        new("PRESSURE_CH01", "kPa", 101.3, 80.0, 130.0, 0.12, 0.35, 45, 0.0002, -2.0, 2.0),
        new("LIGHT_CH01", "lux", 580.0, 0.0, 2000.0, 2.5, 25.0, 20, 0.02, -80.0, 120.0),
        new("VOLTAGE_CH01", "V", 12.0, 0.0, 30.0, 0.03, 0.04, 15, 0.0, -0.5, 0.5),
        new("CURRENT_CH01", "A", 1.2, 0.0, 5.0, 0.02, 0.08, 18, 0.0, -0.2, 0.4),
        new("VIBRATION_CH01", "mm/s", 0.03, 0.0, 10.0, 0.01, 0.01, 8, 0.0, 0.0, 0.2)
    ];

    public static MatrixSimulationSpec CreateMatrix() =>
        new(16, 16, "LightIntensity", "lux", 520.0, 120.0, 85.0, 4.0);
}
