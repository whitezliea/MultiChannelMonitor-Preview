namespace Domain.Common;

public static class UtcDateTime
{
    public static DateTime Require(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                $"{parameterName} must use DateTimeKind.Utc. Actual kind: {value.Kind}.",
                parameterName);
        }

        return value;
    }

    public static DateTime? Require(DateTime? value, string parameterName) =>
        value.HasValue ? Require(value.Value, parameterName) : null;

    public static DateTimeOffset Require(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"{parameterName} must use a zero UTC offset. Actual offset: {value.Offset}.",
                parameterName);
        }

        return value;
    }
}
