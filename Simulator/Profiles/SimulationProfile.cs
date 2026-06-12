namespace Simulator.Profiles;

public sealed record SimulationProfile(
    string Name,
    bool EnableTemperatureDrift = true,
    bool EnableVibrationSpike = true,
    bool EnableOfflineWindow = true,
    bool EnableMatrixHotspot = true);
