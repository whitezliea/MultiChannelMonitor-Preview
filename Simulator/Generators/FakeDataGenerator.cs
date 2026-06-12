using AppLogging;
using Domain.Common;
using Domain.Devices;
using Domain.Measurements;
using Domain.Tags;
using Simulator.Models;
using Simulator.Profiles;
using Simulator.Scenarios;

namespace Simulator.Generators;

public sealed class FakeDataGenerator
{
    private readonly string _deviceId;
    private readonly IReadOnlyList<ChannelSimulationSpec> _channelSpecs;
    private readonly MatrixSimulationSpec _matrixSpec;
    private readonly ChannelValueGenerator _channelGenerator;
    private readonly MatrixValueGenerator _matrixGenerator;
    private readonly ISimulationScenario _scenario;
    private readonly SimulationClock _clock;
    private long _sequenceNo;

    public FakeDataGenerator()
        : this("MCMD-001", new DemoScenario(), DateTime.UtcNow)
    {
    }

    public FakeDataGenerator(string deviceId, ISimulationScenario scenario, DateTime? startTime = null)
        : this(
            deviceId,
            scenario,
            DefaultInstrumentProfile.CreateChannels(),
            DefaultInstrumentProfile.CreateMatrix(),
            startTime ?? DateTime.UtcNow)
    {
    }

    public FakeDataGenerator(
        string deviceId,
        ISimulationScenario scenario,
        IReadOnlyList<ChannelSimulationSpec> channelSpecs,
        MatrixSimulationSpec matrixSpec,
        DateTime startTime)
    {
        _deviceId = deviceId;
        _scenario = scenario;
        _channelSpecs = channelSpecs;
        _matrixSpec = matrixSpec;
        _clock = new SimulationClock(startTime);
        _channelGenerator = new ChannelValueGenerator();
        _matrixGenerator = new MatrixValueGenerator();
    }

    public RawMeasurementFrame NextFrame(DateTime timestamp)
    {
        UtcDateTime.Require(timestamp, nameof(timestamp));
        _sequenceNo++;
        var (elapsed, deltaSeconds) = _clock.Advance(timestamp);
        var deviceEffect = _scenario.GetDeviceEffect(elapsed, _sequenceNo);
        var channels = new List<ChannelValue>();

        foreach (var spec in _channelSpecs)
        {
            var effect = _scenario.GetChannelEffect(spec.Code, elapsed, _sequenceNo);
            channels.Add(_channelGenerator.Generate(spec, elapsed, deltaSeconds, effect));
        }

        var matrixEffect = _scenario.GetMatrixEffect(elapsed, _sequenceNo);
        var matrix = _matrixGenerator.Generate(_matrixSpec, timestamp, elapsed, matrixEffect);
        var quality = ResolveFrameQuality(channels, deviceEffect);
        var status = deviceEffect.ForcedStatus ?? ResolveDeviceStatus(quality);

        AppLogger.Info("FakeDataGenerator | NextFrame | Generated frame _sequenceNo：{0}， timestamp：{1}，quality：{2}，status：{3}",  _sequenceNo, timestamp, quality, status);

        return new RawMeasurementFrame(
            Guid.NewGuid(),
            _deviceId,
            _sequenceNo,
            timestamp,
            status,
            channels,
            matrix,
            deviceEffect.ErrorCode,
            quality);
    }

    private static TagQuality ResolveFrameQuality(IReadOnlyList<ChannelValue> channels, DeviceEffect deviceEffect)
    {
        if (deviceEffect.ForcedFrameQuality.HasValue)
        {
            return deviceEffect.ForcedFrameQuality.Value;
        }

        if (channels.All(channel => channel.Quality == TagQuality.Good))
        {
            return TagQuality.Good;
        }

        if (channels.Any(channel => channel.Quality == TagQuality.Offline))
        {
            return TagQuality.Offline;
        }

        if (channels.Any(channel => channel.Quality == TagQuality.Timeout))
        {
            return TagQuality.Timeout;
        }

        if (channels.Any(channel => channel.Quality == TagQuality.DeviceError))
        {
            return TagQuality.DeviceError;
        }

        if (channels.Any(channel => channel.Quality == TagQuality.OutOfRange))
        {
            return TagQuality.OutOfRange;
        }

        return TagQuality.Bad;
    }

    private static DeviceStatus ResolveDeviceStatus(TagQuality quality) => quality switch
    {
        TagQuality.Good => DeviceStatus.Running,
        TagQuality.DeviceError => DeviceStatus.Error,
        TagQuality.Offline => DeviceStatus.Offline,
        _ => DeviceStatus.Warning
    };
}
