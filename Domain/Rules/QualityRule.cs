using Domain.Devices;
using Domain.Tags;

namespace Domain.Rules;

public static class QualityRule
{
    public static TagQuality FromDeviceState(DeviceStatus status, int errorCode)
    {
        if (status == DeviceStatus.Offline)
        {
            return TagQuality.Offline;
        }

        return errorCode == 0 ? TagQuality.Good : TagQuality.DeviceError;
    }
}
