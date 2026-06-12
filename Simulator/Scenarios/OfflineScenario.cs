using Domain.Devices;
using Domain.Tags;

namespace Simulator.Scenarios;

public sealed class OfflineScenario : ISimulationScenario
{
    public string Name => "Offline";

    public DeviceEffect GetDeviceEffect(TimeSpan elapsed, long sequenceNo) =>
        new(DeviceStatus.Offline, TagQuality.Offline, ErrorCode: 1001);

    public ChannelEffect GetChannelEffect(string channelCode, TimeSpan elapsed, long sequenceNo) =>
        new(ForcedQuality: TagQuality.Offline, ErrorCode: 1001, MissingValue: true);

    public MatrixEffect GetMatrixEffect(TimeSpan elapsed, long sequenceNo) =>
        new(ForcedQuality: TagQuality.Offline);
}
