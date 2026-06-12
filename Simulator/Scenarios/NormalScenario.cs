using Domain.Devices;

namespace Simulator.Scenarios;

public sealed class NormalScenario : ISimulationScenario
{
    public string Name => "Normal";

    public DeviceEffect GetDeviceEffect(TimeSpan elapsed, long sequenceNo) => new(DeviceStatus.Running);

    public ChannelEffect GetChannelEffect(string channelCode, TimeSpan elapsed, long sequenceNo) => new();

    public MatrixEffect GetMatrixEffect(TimeSpan elapsed, long sequenceNo) => new();
}
