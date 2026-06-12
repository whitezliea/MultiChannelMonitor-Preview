using Domain.Devices;
using Domain.Tags;

namespace Simulator.Scenarios;

public sealed record DeviceEffect(
    DeviceStatus? ForcedStatus = null,
    TagQuality? ForcedFrameQuality = null,
    int ErrorCode = 0,
    bool SuppressFrame = false);

public sealed record ChannelEffect(
    double Offset = 0,
    double Scale = 1,
    double? OverrideValue = null,
    TagQuality? ForcedQuality = null,
    int ErrorCode = 0,
    bool MissingValue = false);

public sealed record MatrixEffect(
    bool AddHotspot = false,
    int HotspotRow = 8,
    int HotspotColumn = 8,
    double HotspotAmplitude = 0,
    bool AddLowRegion = false,
    int LowRegionRow = 4,
    int LowRegionColumn = 4,
    double LowRegionScale = 1.0,
    TagQuality? ForcedQuality = null);
