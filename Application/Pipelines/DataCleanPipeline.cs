using Application.Services;
using Application.Services.MeasurementMap;
using AppLogging;
using Domain.Devices;
using Domain.Measurements;
using Domain.Tags;
using System.Net.NetworkInformation;

namespace Application.Pipelines;

public sealed class DataCleanPipeline
{
    private readonly IReadOnlyDictionary<string, TagDefinition> _definitions;
    private readonly IReadOnlyList<TagSourceMapping> _mappings;
    private readonly AbnormalPointDetector _abnormalPointDetector;
    private readonly MatrixAbnormalDetectionOptions _matrixDetectionOptions;
    private readonly Dictionary<string, (DateTime Timestamp, long SequenceNo)> _lastFrames = [];

    public DataCleanPipeline(IEnumerable<TagDefinition> definitions)
        : this(definitions, TagDefinitionCatalog.CreateSourceMappings())
    {
        AppLogger.Info("DataCleanPipeline | DataCleanPipeline init!!");
    }

    public DataCleanPipeline(IEnumerable<TagDefinition> definitions, IEnumerable<TagSourceMapping> mappings)
        : this(
            definitions,
            mappings,
            new AbnormalPointDetector(),
            MatrixAbnormalDetectionOptions.Default)
    {
    }

    public DataCleanPipeline(
        IEnumerable<TagDefinition> definitions,
        IEnumerable<TagSourceMapping> mappings,
        AbnormalPointDetector abnormalPointDetector,
        MatrixAbnormalDetectionOptions matrixDetectionOptions)
    {
        _definitions = definitions.ToDictionary(definition => definition.TagId, StringComparer.Ordinal);
        _mappings = mappings.Where(mapping => mapping.IsEnabled).ToArray();
        _abnormalPointDetector = abnormalPointDetector ?? throw new ArgumentNullException(nameof(abnormalPointDetector));
        _matrixDetectionOptions = matrixDetectionOptions ?? throw new ArgumentNullException(nameof(matrixDetectionOptions));
    }

    public IReadOnlyList<TagValue> Clean(RawMeasurementFrame frame)
    {
        var cleanedValues = CleanToCleanedValues(frame);

        return cleanedValues
            .Select(value =>
            {
                var numericValue = value.NumericValue
                    ?? (value.BoolValue.HasValue ? value.BoolValue.Value ? 1d : 0d : 0d);
                var alarmState = EvaluateAlarmState(numericValue, value.Quality, _definitions.GetValueOrDefault(value.TagId));

                // AppLogger.Info("DataCleanPipeline | Generated frame _sequenceNo：{0}， timestamp：{1}，quality：{2}，TagId：{3}", value.SequenceNo, value.Timestamp.UtcDateTime, value.Quality, value.TagId);

                return new TagValue(
                    value.TagId,
                    numericValue,
                    value.Timestamp.UtcDateTime,
                    value.Quality,
                    alarmState,
                    value.SourceDeviceId,
                    value.SequenceNo);
            })
            .ToArray();
    }

    public void ResetSession() => _lastFrames.Clear();

    public IReadOnlyList<CleanedTagValue> CleanToCleanedValues(RawMeasurementFrame frame)
    {
        MeasurementTimeContract.Validate(frame);
        var values = new List<CleanedTagValue>();
        AddFrameFieldTags(frame, values);
        AddChannelTags(frame, values);
        AddDerivedTags(frame, values);
        AddMatrixStatisticTags(frame, values);

        _lastFrames[frame.DeviceId] = (frame.Timestamp, frame.SequenceNo);
        AppLogger.Info("DataCleanPipeline | CleanToCleandValues | TimeStamp : {0}, SequenceNo: {1}",frame.Timestamp,frame.SequenceNo);
        return values;
    }

    public IReadOnlyList<TagRuntimeState> ToRuntimeStates(RawMeasurementFrame frame, DateTimeOffset lastUpdateTime)
    {
        return CleanToCleanedValues(frame)
            .Select(value =>
            {
                var definition = _definitions.GetValueOrDefault(value.TagId);
                var numericValue = value.NumericValue
                    ?? (value.BoolValue.HasValue ? value.BoolValue.Value ? 1d : 0d : null);
                return new TagRuntimeState(
                    value.TagId,
                    definition?.DisplayName ?? value.TagId,
                    definition?.Category ?? TagCategory.Runtime,
                    value.NumericValue,
                    value.TextValue,
                    value.BoolValue,
                    definition?.Unit ?? value.Unit,
                    definition?.DataType ?? value.DataType,
                    value.Quality,
                    EvaluateAlarmState(numericValue ?? 0d, value.Quality, definition),
                    value.Timestamp,
                    value.SourceFrameId,
                    value.SequenceNo,
                    lastUpdateTime);
            })
            .ToArray();
    }

    private void AddFrameFieldTags(RawMeasurementFrame frame, List<CleanedTagValue> values)
    {
        foreach (var mapping in _mappings.Where(mapping => mapping.SourceType is SourceType.FrameField or SourceType.Runtime))
        {
            var definition = _definitions.GetValueOrDefault(mapping.TagId);
            if (definition is null)
            {
                continue;
            }

            var quality = frame.DeviceStatus == DeviceStatus.Offline ? TagQuality.Offline : frame.Quality;
            var sourcePath = mapping.SourcePath ?? "";
            double? numericValue = null;
            string? textValue = null;
            bool? boolValue = null;

            switch (sourcePath)
            {
                case "DeviceStatus":
                    if (mapping.TagId == "DEVICE.ONLINE")
                    {
                        boolValue = frame.DeviceStatus != DeviceStatus.Offline;
                    }
                    else
                    {
                        textValue = frame.DeviceStatus.ToString();
                    }
                    break;
                case "ErrorCode":
                    numericValue = frame.ErrorCode;
                    break;
                case "Quality":
                    textValue = frame.Quality.ToString();
                    break;
                case "SequenceNo":
                    numericValue = frame.SequenceNo;
                    break;
                case "TimestampDelta":
                    numericValue = _lastFrames.TryGetValue(frame.DeviceId, out var lastTimestamp)
                        ? Math.Max(0, (frame.Timestamp - lastTimestamp.Timestamp).TotalMilliseconds)
                        : 0;
                    break;
                case "SequenceNoDelta":
                    numericValue = _lastFrames.TryGetValue(frame.DeviceId, out var lastSequence)
                        ? Math.Max(0, frame.SequenceNo - lastSequence.SequenceNo - 1)
                        : 0;
                    break;
                default:
                    continue;
            }

            values.Add(CreateCleanedValue(frame, definition, mapping, numericValue, textValue, boolValue, quality, null));
        }
    }

    private void AddChannelTags(RawMeasurementFrame frame, List<CleanedTagValue> values)
    {
        var channelMap = frame.ChannelValues.ToDictionary(channel => channel.ChannelId, StringComparer.Ordinal);

        foreach (var mapping in _mappings.Where(mapping => mapping.SourceType == SourceType.Channel))
        {
            if (mapping.SourceCode is null || !_definitions.TryGetValue(mapping.TagId, out var definition))
            {
                continue;
            }

            if (!channelMap.TryGetValue(mapping.SourceCode, out var channel))
            {
                continue;
            }

            var valueIsMissing = double.IsNaN(channel.Value);
            double? numericValue = valueIsMissing ? null : channel.Value * mapping.Scale + mapping.Offset;
            var quality = EvaluateChannelQuality(frame, channel, definition, numericValue);
            var cleanMessage = valueIsMissing ? "Raw channel value is missing." : null;

            values.Add(CreateCleanedValue(frame, definition, mapping, numericValue, null, null, quality, cleanMessage));
        }
    }

    private void AddDerivedTags(RawMeasurementFrame frame, List<CleanedTagValue> values)
    {
        var byTagId = values.ToDictionary(value => value.TagId, StringComparer.Ordinal);
        AddDerivedNumeric(frame, values, "MEAS.POWER.CH01", ["MEAS.VOLTAGE.CH01", "MEAS.CURRENT.CH01"], input => input[0] * input[1]);
        AddDerivedNumeric(frame, values, "MEAS.LOAD_RATIO.CH01", ["MEAS.CURRENT.CH01"], input => input[0] / 5.0 * 100.0);

        void AddDerivedNumeric(
            RawMeasurementFrame sourceFrame,
            List<CleanedTagValue> targetValues,
            string tagId,
            string[] inputTagIds,
            Func<double[], double> calculate)
        {
            if (!_definitions.TryGetValue(tagId, out var definition))
            {
                return;
            }

            var inputs = inputTagIds.Select(id => byTagId.GetValueOrDefault(id)).ToArray();
            var inputValues = inputs.Select(input => input?.NumericValue).ToArray();
            var quality = inputs.Any(input => input?.Quality == TagQuality.Offline)
                ? TagQuality.Offline
                : inputs.All(input => input?.Quality == TagQuality.Good) && inputValues.All(value => value.HasValue)
                    ? TagQuality.Good
                    : TagQuality.Bad;
            double? numericValue = quality == TagQuality.Good
                ? calculate(inputValues.Select(value => value!.Value).ToArray())
                : null;
            var mapping = _mappings.First(mapping => mapping.TagId == tagId);
            var rangedQuality = numericValue.HasValue
                ? ApplyRangeQuality(numericValue.Value, quality, definition)
                : quality;

            targetValues.Add(CreateCleanedValue(sourceFrame, definition, mapping, numericValue, null, null, rangedQuality, null));
            byTagId[tagId] = targetValues[^1];
        }
    }

    private void AddMatrixStatisticTags(RawMeasurementFrame frame, List<CleanedTagValue> values)
    {
        if (frame.MatrixValues is null)
        {
            return;
        }

        var matrix = frame.MatrixValues;
        var statistics = matrix.CalculateStatistics();
        var abnormalPoints = _abnormalPointDetector.Detect(matrix, statistics, _matrixDetectionOptions);
        var (hotspotRow, hotspotColumn) = FindHotspot(matrix);
        var stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["MATRIX.LIGHT.AVG"] = statistics.AverageValue,
            ["MATRIX.LIGHT.MAX"] = statistics.MaxValue,
            ["MATRIX.LIGHT.MIN"] = statistics.MinValue,
            ["MATRIX.LIGHT.UNIFORMITY"] = statistics.Uniformity,
            ["MATRIX.LIGHT.ABNORMAL_COUNT"] = abnormalPoints.Count,
            ["MATRIX.LIGHT.HOTSPOT_ROW"] = hotspotRow,
            ["MATRIX.LIGHT.HOTSPOT_COL"] = hotspotColumn
        };

        foreach (var mapping in _mappings.Where(mapping => mapping.SourceType == SourceType.Matrix))
        {
            if (!_definitions.TryGetValue(mapping.TagId, out var definition) || !stats.TryGetValue(mapping.TagId, out var numericValue))
            {
                continue;
            }

            var quality = frame.DeviceStatus == DeviceStatus.Offline ? TagQuality.Offline : frame.Quality;
            quality = ApplyRangeQuality(numericValue, quality, definition);
            values.Add(CreateCleanedValue(frame, definition, mapping, numericValue, null, null, quality, null));
        }
    }

    private static (int HotspotRow, int HotspotColumn) FindHotspot(MatrixFrame matrix)
    {
        var max = double.NegativeInfinity;
        var hotspotRow = 0;
        var hotspotColumn = 0;

        for (var row = 0; row < matrix.Rows; row++)
        {
            for (var column = 0; column < matrix.Columns; column++)
            {
                var value = matrix.Values[row, column];
                if (double.IsFinite(value) && value > max)
                {
                    max = value;
                    hotspotRow = row;
                    hotspotColumn = column;
                }

            }
        }

        return (hotspotRow, hotspotColumn);
    }

    private static CleanedTagValue CreateCleanedValue(
        RawMeasurementFrame frame,
        TagDefinition definition,
        TagSourceMapping mapping,
        double? numericValue,
        string? textValue,
        bool? boolValue,
        TagQuality quality,
        string? cleanMessage)
    {
        return new CleanedTagValue(
            definition.TagId,
            numericValue,
            textValue,
            boolValue,
            definition.DataType,
            definition.Unit,
            new DateTimeOffset(frame.Timestamp),
            quality,
            frame.DeviceId,
            mapping.SourceCode,
            frame.FrameId,
            frame.SequenceNo,
            cleanMessage);
    }

    private static TagQuality EvaluateChannelQuality(RawMeasurementFrame frame, ChannelValue channel, TagDefinition definition, double? numericValue)
    {
        if (frame.DeviceStatus == DeviceStatus.Offline)
        {
            return TagQuality.Offline;
        }

        if (channel.Quality != TagQuality.Good)
        {
            return channel.Quality;
        }

        if (!numericValue.HasValue)
        {
            return TagQuality.Bad;
        }

        if (frame.ErrorCode != 0 || frame.Quality == TagQuality.DeviceError)
        {
            return TagQuality.DeviceError;
        }

        return ApplyRangeQuality(numericValue.Value, frame.Quality, definition);
    }

    private static TagQuality ApplyRangeQuality(double value, TagQuality quality, TagDefinition definition)
    {
        if (quality != TagQuality.Good)
        {
            return quality;
        }

        if (definition.MinValue.HasValue && value < definition.MinValue.Value)
        {
            return TagQuality.OutOfRange;
        }

        if (definition.MaxValue.HasValue && value > definition.MaxValue.Value)
        {
            return TagQuality.OutOfRange;
        }

        return quality;
    }

    private static TagAlarmState EvaluateAlarmState(double value, TagQuality quality, TagDefinition? definition)
    {
        if (quality == TagQuality.Offline)
        {
            return TagAlarmState.Offline;
        }

        if (quality != TagQuality.Good)
        {
            return TagAlarmState.Invalid;
        }

        if (definition is null)
        {
            return TagAlarmState.Normal;
        }

        if (definition.AlarmHigh.HasValue && value >= definition.AlarmHigh.Value)
        {
            return TagAlarmState.AlarmHigh;
        }

        if (definition.AlarmLow.HasValue && value <= definition.AlarmLow.Value)
        {
            return TagAlarmState.AlarmLow;
        }

        if (definition.WarningHigh.HasValue && value >= definition.WarningHigh.Value)
        {
            return TagAlarmState.WarningHigh;
        }

        if (definition.WarningLow.HasValue && value <= definition.WarningLow.Value)
        {
            return TagAlarmState.WarningLow;
        }

        return TagAlarmState.Normal;
    }
}
