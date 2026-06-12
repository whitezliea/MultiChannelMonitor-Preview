using Application.Abstractions.DataSource;
using Application.Abstractions.Persistence;
using Application.Abstractions.Time;
using Application.Configuration;
using Infrastructure.Persistence;
using Infrastructure.System;
using Simulator.Adapters;
using Simulator.Generators;

namespace Presentation.Wpf.Bootstrap;

/// <summary>
/// 组合根的可替换边界。生产环境使用 SQLite 和模拟数据源，测试可传入内存仓储、测试时钟或假数据源。
/// </summary>
public sealed record RuntimeCompositionDependencies(
    IClock Clock,
    IHistoryRepository HistoryRepository,
    IAlarmRepository AlarmRepository,
    IOperationLogRepository OperationLogRepository,
    IConfigurationRepository ConfigurationRepository,
    Func<CancellationToken, Task> InitializePersistenceAsync,
    Func<IRuntimeOptionsStore, IClock, IDataSource> DataSourceFactory,
    SqliteConnectionFactory? DatabaseConnectionFactory = null)
{
    public static RuntimeCompositionDependencies CreateDefault()
    {
        var connectionFactory = new SqliteConnectionFactory();
        return new RuntimeCompositionDependencies(
            new SystemClock(),
            new SQLiteHistoryRepository(connectionFactory),
            new SQLiteAlarmRepository(connectionFactory),
            new SQLiteOperationLogRepository(connectionFactory),
            new SQLiteConfigurationRepository(connectionFactory),
            connectionFactory.InitializeAsync,
            static (optionsStore, clock) =>
                new SimulatorDataSource(new FakeDataGenerator(), optionsStore, clock),
            connectionFactory);
    }
}
