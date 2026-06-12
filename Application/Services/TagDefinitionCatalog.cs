using Domain.Tags;

namespace Application.Services;

public static class TagDefinitionCatalog
{
    private const string DefaultDeviceId = "MCMD-001";

    public static IReadOnlyList<TagDefinition> CreateDefaults() =>
    [
        new("DEVICE.STATUS", "设备运行状态", TagCategory.Device, "", IsHistorized: false, Description: "设备当前运行状态", DataType: TagDataType.Enum, ValueKind: TagValueKind.Enum, DisplayOrder: 10),
        new("DEVICE.ONLINE", "设备在线状态", TagCategory.Device, "", Description: "由 DeviceStatus != Offline 推导", DataType: TagDataType.Boolean, ValueKind: TagValueKind.Boolean, DisplayOrder: 20),
        new("DEVICE.ERROR_CODE", "设备错误码", TagCategory.Device, "", 0, 9999, Description: "0 表示无错误", DataType: TagDataType.Int, DisplayOrder: 30),
        new("DEVICE.QUALITY", "设备帧质量", TagCategory.Device, "", Description: "Raw frame quality 映射", DataType: TagDataType.Enum, ValueKind: TagValueKind.Enum, DisplayOrder: 40),
        new("DEVICE.SEQUENCE_NO", "最新帧序号", TagCategory.Device, "", 0, IsHistorized: false, Description: "设备帧序号", DataType: TagDataType.Int, DisplayOrder: 50),
        new("DEVICE.FRAME_INTERVAL_MS", "帧间隔", TagCategory.Runtime, "ms", 0, 5000, 750, 1500, Description: "根据连续帧 Timestamp 计算", DisplayOrder: 60),
        new("DEVICE.FRAME_LOSS_COUNT", "连续丢帧数", TagCategory.Runtime, "frames", 0, WarningHigh: 1, AlarmHigh: 3, Description: "根据 SequenceNo 跳号计算", DataType: TagDataType.Int, DisplayOrder: 70),

        new("MEAS.TEMP.CH01", "温度 CH01", TagCategory.Measurement, "℃", -20, 120, 60, 80, 5, 0, Description: "温度超限演示主 Tag", DisplayOrder: 110),
        new("MEAS.PRESSURE.CH01", "压力 CH01", TagCategory.Measurement, "kPa", 80, 130, 115, 125, 90, 85, Description: "压力稳定性监控", DisplayOrder: 120),
        new("MEAS.LIGHT.CH01", "光强 CH01", TagCategory.Measurement, "lux", 0, 2000, 1500, 1800, 100, 50, Description: "光强单点监控", DisplayOrder: 130),
        new("MEAS.VOLTAGE.CH01", "电压 CH01", TagCategory.Electrical, "V", 0, 30, 14, 15, 10.5, 9.5, Description: "电压跌落演示 Tag", DisplayOrder: 140),
        new("MEAS.CURRENT.CH01", "电流 CH01", TagCategory.Electrical, "A", 0, 5, 3, 4, 0.2, 0.1, Description: "负载电流监控", DisplayOrder: 150),
        new("MEAS.VIBRATION.CH01", "振动 CH01", TagCategory.Mechanical, "mm/s", 0, 10, 1.0, 2.5, Description: "振动尖峰演示 Tag", DisplayOrder: 160),

        new("MEAS.POWER.CH01", "功率 CH01", TagCategory.Derived, "W", 0, 150, 36, 48, Description: "电压与电流派生功率", ValueKind: TagValueKind.DerivedNumeric, DisplayOrder: 210),
        new("MEAS.LOAD_RATIO.CH01", "负载率 CH01", TagCategory.Derived, "%", 0, 100, 70, 85, Description: "以电流量程 5A 计算", ValueKind: TagValueKind.DerivedNumeric, DisplayOrder: 220),

        new("MATRIX.LIGHT.AVG", "矩阵平均光强", TagCategory.Matrix, "lux", 0, 2000, 1200, 1600, 300, 100, Description: "热力图概览指标", ValueKind: TagValueKind.MatrixStat, DisplayOrder: 310),
        new("MATRIX.LIGHT.MAX", "矩阵最大光强", TagCategory.Matrix, "lux", 0, 2500, 1500, 1800, Description: "局部热点判断指标", ValueKind: TagValueKind.MatrixStat, DisplayOrder: 320),
        new("MATRIX.LIGHT.MIN", "矩阵最小光强", TagCategory.Matrix, "lux", 0, 2000, WarningLow: 100, AlarmLow: 50, Description: "局部低值判断指标", ValueKind: TagValueKind.MatrixStat, DisplayOrder: 330),
        new("MATRIX.LIGHT.UNIFORMITY", "矩阵均匀性", TagCategory.Matrix, "ratio", 0, 1, WarningLow: 0.70, AlarmLow: 0.55, Description: "越接近 1 越均匀", ValueKind: TagValueKind.MatrixStat, DisplayOrder: 340),
        new("MATRIX.LIGHT.ABNORMAL_COUNT", "矩阵异常点数量", TagCategory.Matrix, "points", 0, 256, WarningHigh: 5, AlarmHigh: 20, Description: "用于热点/暗区快速告警", DataType: TagDataType.Int, ValueKind: TagValueKind.MatrixStat, DisplayOrder: 350),
        new("MATRIX.LIGHT.HOTSPOT_ROW", "热点行坐标", TagCategory.Matrix, "row", 0, 15, IsHistorized: false, Description: "辅助热力图定位", DataType: TagDataType.Int, ValueKind: TagValueKind.MatrixStat, DisplayOrder: 360),
        new("MATRIX.LIGHT.HOTSPOT_COL", "热点列坐标", TagCategory.Matrix, "col", 0, 15, IsHistorized: false, Description: "辅助热力图定位", DataType: TagDataType.Int, ValueKind: TagValueKind.MatrixStat, DisplayOrder: 370)
    ];

    public static IReadOnlyList<TagSourceMapping> CreateSourceMappings(string sourceDeviceId = DefaultDeviceId) =>
    [
        new("DEVICE.STATUS", sourceDeviceId, SourceType.FrameField, SourcePath: "DeviceStatus"),
        new("DEVICE.ONLINE", sourceDeviceId, SourceType.FrameField, SourcePath: "DeviceStatus"),
        new("DEVICE.ERROR_CODE", sourceDeviceId, SourceType.FrameField, SourcePath: "ErrorCode"),
        new("DEVICE.QUALITY", sourceDeviceId, SourceType.FrameField, SourcePath: "Quality"),
        new("DEVICE.SEQUENCE_NO", sourceDeviceId, SourceType.FrameField, SourcePath: "SequenceNo"),
        new("DEVICE.FRAME_INTERVAL_MS", sourceDeviceId, SourceType.Runtime, SourcePath: "TimestampDelta"),
        new("DEVICE.FRAME_LOSS_COUNT", sourceDeviceId, SourceType.Runtime, SourcePath: "SequenceNoDelta"),

        new("MEAS.TEMP.CH01", sourceDeviceId, SourceType.Channel, SourceCode: "TEMP_CH01"),
        new("MEAS.PRESSURE.CH01", sourceDeviceId, SourceType.Channel, SourceCode: "PRESSURE_CH01"),
        new("MEAS.LIGHT.CH01", sourceDeviceId, SourceType.Channel, SourceCode: "LIGHT_CH01"),
        new("MEAS.VOLTAGE.CH01", sourceDeviceId, SourceType.Channel, SourceCode: "VOLTAGE_CH01"),
        new("MEAS.CURRENT.CH01", sourceDeviceId, SourceType.Channel, SourceCode: "CURRENT_CH01"),
        new("MEAS.VIBRATION.CH01", sourceDeviceId, SourceType.Channel, SourceCode: "VIBRATION_CH01"),

        new("MEAS.POWER.CH01", sourceDeviceId, SourceType.Derived, Formula: "MEAS.VOLTAGE.CH01 * MEAS.CURRENT.CH01", InputTagIds: "MEAS.VOLTAGE.CH01,MEAS.CURRENT.CH01"),
        new("MEAS.LOAD_RATIO.CH01", sourceDeviceId, SourceType.Derived, Formula: "MEAS.CURRENT.CH01 / 5.0 * 100", InputTagIds: "MEAS.CURRENT.CH01"),

        new("MATRIX.LIGHT.AVG", sourceDeviceId, SourceType.Matrix, SourcePath: "Average(MatrixValues)"),
        new("MATRIX.LIGHT.MAX", sourceDeviceId, SourceType.Matrix, SourcePath: "Max(MatrixValues)"),
        new("MATRIX.LIGHT.MIN", sourceDeviceId, SourceType.Matrix, SourcePath: "Min(MatrixValues)"),
        new("MATRIX.LIGHT.UNIFORMITY", sourceDeviceId, SourceType.Matrix, SourcePath: "Uniformity(MatrixValues)"),
        new("MATRIX.LIGHT.ABNORMAL_COUNT", sourceDeviceId, SourceType.Matrix, SourcePath: "Count(MatrixValues outside warning range)"),
        new("MATRIX.LIGHT.HOTSPOT_ROW", sourceDeviceId, SourceType.Matrix, SourcePath: "ArgMax.Row"),
        new("MATRIX.LIGHT.HOTSPOT_COL", sourceDeviceId, SourceType.Matrix, SourcePath: "ArgMax.Col")
    ];
}
