using Domain.Devices;
using Domain.Tags;

namespace Domain.Measurements;

public sealed record RawMeasurementFrame(
    Guid FrameId,
    string DeviceId,
    long SequenceNo,
    DateTime Timestamp,
    DeviceStatus DeviceStatus,
    IReadOnlyList<ChannelValue> ChannelValues,
    MatrixFrame? MatrixValues,
    int ErrorCode,
    TagQuality Quality);
