using Application.Abstractions.Time;
using Domain.Common;

namespace Tests.Support;

internal sealed class TestClock : IClock
{
    public TestClock(DateTime utcNow)
    {
        UtcNow = UtcDateTime.Require(utcNow, nameof(utcNow));
    }

    public DateTime UtcNow { get; set; }
}
