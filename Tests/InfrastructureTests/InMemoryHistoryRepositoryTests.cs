using Domain.Tags;
using Infrastructure.Persistence;

namespace Tests.InfrastructureTests;

public class InMemoryHistoryRepositoryTests
{
    [Fact]
    public async Task QueryAsync_ReturnsSamplesInsideTimeRange()
    {
        var repository = new InMemoryHistoryRepository();
        var timestamp = DateTime.UtcNow;
        await repository.AppendAsync([
            new TagValue("MEAS.TEMP.CH01", 25, timestamp, TagQuality.Good, TagAlarmState.Normal, "test", 1),
            new TagValue("MEAS.PRESSURE.CH01", 101, timestamp, TagQuality.Good, TagAlarmState.Normal, "test", 1)
        ], CancellationToken.None);

        var result = await repository.QueryAsync("MEAS.TEMP.CH01", timestamp.AddSeconds(-1), timestamp.AddSeconds(1), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(25, result[0].Value);
    }
}
