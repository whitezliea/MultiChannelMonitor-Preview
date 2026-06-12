namespace Simulator.Profiles;

public static class DefaultProfiles
{
    public static SimulationProfile Demo { get; } = new("Demo");
    public static SimulationProfile Normal { get; } = new("Normal", EnableTemperatureDrift: false, EnableVibrationSpike: false, EnableOfflineWindow: false, EnableMatrixHotspot: false);
    public static SimulationProfile Alarm { get; } = new("Alarm", EnableOfflineWindow: false);
}
