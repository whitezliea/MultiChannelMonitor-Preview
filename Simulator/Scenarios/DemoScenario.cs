using Domain.Devices;
using Domain.Tags;

namespace Simulator.Scenarios;

public sealed class DemoScenario : ISimulationScenario
{
    private const double CycleSeconds = 120.0;

    public string Name => "Interview Demo";

    public DeviceEffect GetDeviceEffect(TimeSpan elapsed, long sequenceNo)
    {
        var t = elapsed.TotalSeconds % CycleSeconds;

        if (t is >= 90 and < 96)
        {
            return new DeviceEffect(DeviceStatus.Offline, TagQuality.Offline, ErrorCode: 1001);
        }

        if (t is >= 70 and < 75)
        {
            return new DeviceEffect(DeviceStatus.Error, TagQuality.DeviceError, ErrorCode: 2001);
        }

        return new DeviceEffect(DeviceStatus.Running);
    }

    public ChannelEffect GetChannelEffect(string channelCode, TimeSpan elapsed, long sequenceNo)
    {
        var t = elapsed.TotalSeconds % CycleSeconds;

        if (channelCode == "TEMP_CH01" && t is >= 20 and < 35)
        {
            return new ChannelEffect(Offset: (t - 20) * 1.2);
        }

        if (channelCode == "VIBRATION_CH01" && t is >= 40 and < 42)
        {
            return new ChannelEffect(OverrideValue: 3.5);
        }

        if (channelCode == "VOLTAGE_CH01" && t is >= 55 and < 60)
        {
            return new ChannelEffect(Offset: -2.8);
        }

        if (channelCode == "LIGHT_CH01" && t is >= 65 and < 68)
        {
            return new ChannelEffect(ForcedQuality: TagQuality.DeviceError, ErrorCode: 3001, MissingValue: true);
        }

        if (t is >= 90 and < 96)
        {
            return new ChannelEffect(ForcedQuality: TagQuality.Offline, ErrorCode: 1001, MissingValue: true);
        }

        return new ChannelEffect();
    }

    public MatrixEffect GetMatrixEffect(TimeSpan elapsed, long sequenceNo)
    {
        var t = elapsed.TotalSeconds % CycleSeconds;

        if (t is >= 80 and < 88)
        {
            return new MatrixEffect(AddHotspot: true, HotspotRow: 9, HotspotColumn: 10, HotspotAmplitude: 350);
        }

        if (t is >= 100 and < 108)
        {
            return new MatrixEffect(AddLowRegion: true, LowRegionRow: 5, LowRegionColumn: 5, LowRegionScale: 0.55);
        }

        return new MatrixEffect();
    }
}
