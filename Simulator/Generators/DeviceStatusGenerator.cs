using Domain.Devices;

namespace Simulator.Generators;

public sealed class DeviceStatusGenerator
{
    public DeviceStatus Generate(long sequenceNo) =>
        sequenceNo % 120 is >= 110 ? DeviceStatus.Offline : DeviceStatus.Running;
}
