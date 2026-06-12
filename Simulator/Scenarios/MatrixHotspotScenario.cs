using Domain.Devices;

namespace Simulator.Scenarios;

public sealed class MatrixHotspotScenario : ISimulationScenario
{
    public string Name => "Matrix Hotspot";

    public DeviceEffect GetDeviceEffect(TimeSpan elapsed, long sequenceNo) => new(DeviceStatus.Running);

    public ChannelEffect GetChannelEffect(string channelCode, TimeSpan elapsed, long sequenceNo) => new();

    public MatrixEffect GetMatrixEffect(TimeSpan elapsed, long sequenceNo) =>
        new(AddHotspot: true, HotspotRow: 9, HotspotColumn: 10, HotspotAmplitude: 350);
}
