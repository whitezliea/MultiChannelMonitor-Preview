namespace Simulator.Scenarios;

public sealed class AlarmScenario : ISimulationScenario
{
    public string Name => "Alarm";

    public DeviceEffect GetDeviceEffect(TimeSpan elapsed, long sequenceNo) => new(Domain.Devices.DeviceStatus.Running);

    public ChannelEffect GetChannelEffect(string channelCode, TimeSpan elapsed, long sequenceNo) =>
        channelCode == "TEMP_CH01" ? new ChannelEffect(Offset: 60) : new ChannelEffect();

    public MatrixEffect GetMatrixEffect(TimeSpan elapsed, long sequenceNo) => new();
}
