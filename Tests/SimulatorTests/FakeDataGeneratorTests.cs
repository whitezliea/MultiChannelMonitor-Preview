using Domain.Devices;
using Domain.Tags;
using Simulator.Generators;
using Simulator.Adapters;
using Application.Configuration;
using Simulator.Scenarios;
using Tests.Support;

namespace Tests.SimulatorTests;

public class FakeDataGeneratorTests
{
    [Fact]
    public void NextFrame_IncrementsSequenceNumber()
    {
        var start = DateTime.UtcNow;
        var generator = new FakeDataGenerator("MCMD-TEST", new NormalScenario(), start);

        var first = generator.NextFrame(start.AddMilliseconds(500));
        var second = generator.NextFrame(start.AddMilliseconds(1000));

        Assert.Equal(first.SequenceNo + 1, second.SequenceNo);
    }

    [Fact]
    public void NormalScenario_GeneratesGoodRunningFrame()
    {
        var start = DateTime.UtcNow;
        var generator = new FakeDataGenerator("MCMD-TEST", new NormalScenario(), start);

        var frame = generator.NextFrame(start.AddSeconds(1));

        Assert.Equal(DeviceStatus.Running, frame.DeviceStatus);
        Assert.Equal(TagQuality.Good, frame.Quality);
        Assert.All(frame.ChannelValues, channel => Assert.Equal(TagQuality.Good, channel.Quality));
        Assert.NotNull(frame.MatrixValues);
        Assert.Equal(16, frame.MatrixValues!.Rows);
        Assert.Equal(16, frame.MatrixValues.Columns);
    }

    [Fact]
    public void DemoScenario_ProducesTemperatureRiseWindow()
    {
        var start = DateTime.UtcNow;
        var normalGenerator = new FakeDataGenerator("MCMD-TEST", new NormalScenario(), start);
        var demoGenerator = new FakeDataGenerator("MCMD-TEST", new DemoScenario(), start);

        var normalFrame = normalGenerator.NextFrame(start.AddSeconds(21));
        var demoFrame = demoGenerator.NextFrame(start.AddSeconds(21));

        var normalTemperature = normalFrame.ChannelValues.Single(channel => channel.ChannelId == "TEMP_CH01").Value;
        var demoTemperature = demoFrame.ChannelValues.Single(channel => channel.ChannelId == "TEMP_CH01").Value;

        Assert.True(demoTemperature > normalTemperature + 0.5);
    }

    [Fact]
    public void DemoScenario_ProducesLightChannelDeviceErrorWindow()
    {
        var start = DateTime.UtcNow;
        var generator = new FakeDataGenerator("MCMD-TEST", new DemoScenario(), start);

        var frame = generator.NextFrame(start.AddSeconds(66));
        var light = frame.ChannelValues.Single(channel => channel.ChannelId == "LIGHT_CH01");

        Assert.True(double.IsNaN(light.Value));
        Assert.Equal(TagQuality.DeviceError, light.Quality);
        Assert.Equal(3001, light.ErrorCode);
    }

    [Fact]
    public void OfflineScenario_ProducesOfflineFrameAndChannels()
    {
        var start = DateTime.UtcNow;
        var generator = new FakeDataGenerator("MCMD-TEST", new OfflineScenario(), start);

        var frame = generator.NextFrame(start.AddSeconds(1));

        Assert.Equal(DeviceStatus.Offline, frame.DeviceStatus);
        Assert.Equal(TagQuality.Offline, frame.Quality);
        Assert.All(frame.ChannelValues, channel =>
        {
            Assert.Equal(TagQuality.Offline, channel.Quality);
            Assert.True(double.IsNaN(channel.Value));
        });
    }

    [Fact]
    public void MatrixHotspotScenario_IncreasesHotspotRegion()
    {
        var start = DateTime.UtcNow;
        var normalGenerator = new FakeDataGenerator("MCMD-TEST", new NormalScenario(), start);
        var hotspotGenerator = new FakeDataGenerator("MCMD-TEST", new MatrixHotspotScenario(), start);

        var normalFrame = normalGenerator.NextFrame(start.AddSeconds(1));
        var hotspotFrame = hotspotGenerator.NextFrame(start.AddSeconds(1));

        var normalValue = normalFrame.MatrixValues!.Values[9, 10];
        var hotspotValue = hotspotFrame.MatrixValues!.Values[9, 10];

        Assert.True(hotspotValue > normalValue + 250);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void NextFrame_RejectsNonUtcTimestamp(DateTimeKind kind)
    {
        var start = DateTime.UtcNow;
        var generator = new FakeDataGenerator("MCMD-TEST", new NormalScenario(), start);
        var timestamp = DateTime.SpecifyKind(start.AddSeconds(1), kind);

        Assert.Throws<ArgumentException>(() => generator.NextFrame(timestamp));
    }

    [Fact]
    public async Task SimulatorDataSource_UsesInjectedUtcClockForRawAndMatrixFrames()
    {
        var timestamp = DateTime.UtcNow;
        var source = new SimulatorDataSource(
            new FakeDataGenerator("MCMD-TEST", new NormalScenario(), timestamp.AddSeconds(-1)),
            new MonitorRuntimeOptions { DataGenerateInterval = TimeSpan.FromHours(1) },
            new TestClock(timestamp));
        await using var enumerator = source
            .ReadFramesAsync(CancellationToken.None)
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(timestamp, enumerator.Current.Timestamp);
        Assert.Equal(DateTimeKind.Utc, enumerator.Current.Timestamp.Kind);
        Assert.Equal(timestamp, enumerator.Current.MatrixValues?.Timestamp);
    }

    [Fact]
    public async Task SimulatorDataSource_WhenCanceledDuringDelay_EndsWithoutThrowing()
    {
        var timestamp = DateTime.UtcNow;
        using var cancellation = new CancellationTokenSource();
        var source = new SimulatorDataSource(
            new FakeDataGenerator("MCMD-TEST", new NormalScenario(), timestamp.AddSeconds(-1)),
            new MonitorRuntimeOptions { DataGenerateInterval = TimeSpan.FromHours(1) },
            new TestClock(timestamp));
        await using var enumerator = source
            .ReadFramesAsync(cancellation.Token)
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());

        var nextFrame = enumerator.MoveNextAsync().AsTask();
        cancellation.Cancel();

        Assert.False(await nextFrame.WaitAsync(TimeSpan.FromSeconds(2)));
    }
}
