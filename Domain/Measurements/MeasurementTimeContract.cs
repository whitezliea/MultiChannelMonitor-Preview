using Domain.Common;

namespace Domain.Measurements;

public static class MeasurementTimeContract
{
    public static void Validate(RawMeasurementFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        UtcDateTime.Require(frame.Timestamp, nameof(frame.Timestamp));

        if (frame.MatrixValues is null)
        {
            return;
        }

        UtcDateTime.Require(frame.MatrixValues.Timestamp, nameof(frame.MatrixValues.Timestamp));
        if (frame.MatrixValues.Timestamp != frame.Timestamp)
        {
            throw new ArgumentException(
                "Matrix frame timestamp must match its source raw frame timestamp.",
                nameof(frame));
        }
    }

    public static void Validate(MatrixFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        UtcDateTime.Require(frame.Timestamp, nameof(frame.Timestamp));
    }
}
