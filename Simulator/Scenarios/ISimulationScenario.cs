namespace Simulator.Scenarios;

public interface ISimulationScenario
{
    string Name { get; }

    DeviceEffect GetDeviceEffect(TimeSpan elapsed, long sequenceNo);

    ChannelEffect GetChannelEffect(string channelCode, TimeSpan elapsed, long sequenceNo);

    MatrixEffect GetMatrixEffect(TimeSpan elapsed, long sequenceNo);
}
