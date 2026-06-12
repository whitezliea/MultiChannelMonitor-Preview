using Domain.Devices;

namespace Application.DTOs.Devices;

public sealed record DeviceStatusDto(string DeviceId, DeviceStatus Status, DateTime LastUpdatedAt);
