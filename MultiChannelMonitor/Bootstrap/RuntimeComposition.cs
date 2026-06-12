using Application.Caches;
using Application.BackgroundWorkers;
using Application.Abstractions.Events;
using Application.Abstractions.Persistence;
using Application.Configuration;
using Application.Events;
using Application.Pipelines;
using Application.Queues;
using Application.Services;
using Application.UseCases.Alarms;
using Application.UseCases.Logs;
using Application.UseCases.Settings;
using Application.UseCases.History;
using Application.UseCases.Export;
using Domain.Tags;
using Infrastructure.Persistence;
using Infrastructure.System;
using Infrastructure.Export;
using Simulator.Adapters;
using Simulator.Generators;

namespace Presentation.Wpf.Bootstrap;

public sealed class RuntimeComposition
{
    public RuntimeComposition()
    {
        RuntimeOptionsStore = new RuntimeOptionsStore(new MonitorRuntimeOptions());
        TrendDiagnosisOptions = new TrendDiagnosisOptions();
        Clock = new SystemClock();
        TagDefinitions = TagDefinitionCatalog.CreateDefaults();
        DefinitionMap = TagDefinitions.ToDictionary(item => item.TagId, StringComparer.Ordinal);
        TagRuntimeConfigurationStore = new TagRuntimeConfigurationStore(
            TagDefinitions.Select(TagRuntimeConfiguration.FromDefinition));

        AlarmEventQueue = new AlarmEventQueue();
        OperationLogQueue = new OperationLogQueue();
        DatabaseConnectionFactory = new SqliteConnectionFactory();
        DatabaseConnectionFactory.InitializeAsync().GetAwaiter().GetResult();
        HistoryRepository = new SQLiteHistoryRepository(DatabaseConnectionFactory);
        AlarmRepository = new SQLiteAlarmRepository(DatabaseConnectionFactory);
        OperationLogRepository = new SQLiteOperationLogRepository(DatabaseConnectionFactory);
        ConfigurationRepository = new SQLiteConfigurationRepository(DatabaseConnectionFactory);
        OperationLogService = new OperationLogService(OperationLogRepository, OperationLogQueue, Clock);
        ConfigurationService = new ConfigurationService(
            TagDefinitions,
            ConfigurationRepository,
            TagRuntimeConfigurationStore,
            RuntimeOptionsStore,
            OperationLogService);
        ConfigurationService.LoadAsync().GetAwaiter().GetResult();
        var tagCache = new TagCache(Options.TrendBufferCapacity);
        TagService = new TagService(tagCache, Clock);
        ChartDataService = new ChartDataService(
            TagService,
            DefinitionMap,
            TagRuntimeConfigurationStore,
            TrendDiagnosisOptions);
        AlarmService = new AlarmService(TagDefinitions, TagRuntimeConfigurationStore);
        var openAlarms = AlarmRepository
            .QueryOpenAlarmsAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        AlarmService.RestoreEvents(openAlarms);
        HistorySampleQueue = new HistorySampleQueue();
        HistoryService = new HistoryService(HistoryRepository, HistorySampleQueue, OperationLogService);
        QueryHistorySamplesUseCase = new QueryHistorySamplesUseCase(HistoryService, TagRuntimeConfigurationStore);
        ExportHistoryCsvUseCase = new ExportHistoryCsvUseCase(
            new SimpleCsvExporter(),
            HistoryRepository,
            OperationLogService);
        HistoryRetentionService = new HistoryRetentionService(
            HistoryRepository,
            OperationLogService,
            Clock,
            Options.HistoryRetentionDays,
            Options.HistoryRetentionDeleteBatchSize);
        HistoryPersistWorker = new HistoryPersistWorker(
            HistorySampleQueue,
            HistoryRepository,
            Options.HistoryBatchInterval,
            Options.HistoryMaxBatchSize);
        AlarmPersistWorker = new AlarmPersistWorker(
            AlarmEventQueue,
            AlarmRepository,
            Options.AlarmBatchInterval,
            Options.AlarmMaxBatchSize);
        OperationLogPersistWorker = new OperationLogPersistWorker(
            OperationLogQueue,
            OperationLogRepository,
            Options.OperationLogBatchInterval,
            Options.OperationLogMaxBatchSize);
        PersistenceRuntime = new PersistenceRuntimeCoordinator(
            HistoryPersistWorker,
            AlarmPersistWorker,
            OperationLogPersistWorker);
        ApplicationRuntime = new ApplicationRuntimeHost(
            PersistenceRuntime,
            OperationLogService,
            HistoryRetentionService);
        QueryOperationLogsUseCase = new QueryOperationLogsUseCase(OperationLogService, PersistenceRuntime);
        QueryAlarmsUseCase = new QueryAlarmsUseCase(AlarmRepository, OperationLogService);
        SaveTagRuntimeSettingsUseCase = new SaveTagRuntimeSettingsUseCase(ConfigurationService);
        SaveRuntimeSettingsUseCase = new SaveRuntimeSettingsUseCase(ConfigurationService);
        MeasurementMapService = new MeasurementMapService(new MatrixFrameCache());
        ProcessedFrameStore = new ProcessedFrameSnapshotStore();
        DashboardService = new DashboardService(TagService, AlarmService, Clock);
        UiSnapshotProvider = new UiSnapshotProvider(
            TagService,
            AlarmService,
            DashboardService,
            ChartDataService,
            MeasurementMapService,
            Clock,
            ProcessedFrameStore);

        var eventPublisher = new ApplicationEventPublisher();
        DataSourceHealthMonitor = new DataSourceHealthMonitor(Clock);
        eventPublisher.Register(
            new MeasurementMapFrameConsumer(MeasurementMapService),
            ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register(new TagCacheConsumer(TagService));
        var dataSourceHealthLogConsumer = new DataSourceHealthOperationLogConsumer(OperationLogService);
        eventPublisher.Register<DataSourceTimedOutEvent>(
            dataSourceHealthLogConsumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register<DataSourceRecoveredEvent>(
            dataSourceHealthLogConsumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register(
            new HistoryRuntimeStateConsumer(HistoryService, TagRuntimeConfigurationStore),
            ApplicationEventHandlerFailurePolicy.Isolated);
        var alarmEventConsumer = new AlarmEventConsumer(AlarmEventQueue);
        eventPublisher.Register<AlarmRaisedEvent>(
            alarmEventConsumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register<AlarmUpdatedEvent>(
            alarmEventConsumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register<AlarmRecoveredEvent>(
            alarmEventConsumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register<AlarmAcknowledgedEvent>(
            alarmEventConsumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
        var alarmOperationLogConsumer = new AlarmOperationLogConsumer(OperationLogService);
        eventPublisher.Register<AlarmRaisedEvent>(
            alarmOperationLogConsumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register<AlarmUpdatedEvent>(
            alarmOperationLogConsumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register<AlarmRecoveredEvent>(
            alarmOperationLogConsumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register<AlarmAcknowledgedEvent>(
            alarmOperationLogConsumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
        AcknowledgeAlarmUseCase = new AcknowledgeAlarmUseCase(AlarmService, eventPublisher, Clock);

        var dataSource = new SimulatorDataSource(new FakeDataGenerator(), RuntimeOptionsStore, Clock);
        var dataSourceService = new DataSourceService(dataSource);
        var cleanPipeline = new DataCleanPipeline(TagDefinitions);
        RuntimeService = new MonitoringRuntimeService(
            dataSourceService,
            cleanPipeline,
            AlarmService,
            eventPublisher,
            Clock,
            DataSourceHealthMonitor,
            RuntimeOptionsStore,
            ProcessedFrameStore,
            MeasurementMapService);
        RuntimeLifecycle = new RuntimeLifecycleCoordinator(RuntimeService.RunAsync);
        AcquisitionRuntime = new AcquisitionRuntimeController(
            RuntimeLifecycle,
            PersistenceRuntime,
            OperationLogService);
    }

    public MonitorRuntimeOptions Options => RuntimeOptionsStore.Snapshot;
    public RuntimeOptionsStore RuntimeOptionsStore { get; }
    public TagRuntimeConfigurationStore TagRuntimeConfigurationStore { get; }
    public TrendDiagnosisOptions TrendDiagnosisOptions { get; }
    public SystemClock Clock { get; }
    public IReadOnlyList<TagDefinition> TagDefinitions { get; }
    public IReadOnlyDictionary<string, TagDefinition> DefinitionMap { get; }
    public TagService TagService { get; }
    public ChartDataService ChartDataService { get; }
    public AlarmService AlarmService { get; }
    public AlarmEventQueue AlarmEventQueue { get; }
    public OperationLogQueue OperationLogQueue { get; }
    public AcknowledgeAlarmUseCase AcknowledgeAlarmUseCase { get; }
    public SqliteConnectionFactory DatabaseConnectionFactory { get; }
    public IHistoryRepository HistoryRepository { get; }
    public IAlarmRepository AlarmRepository { get; }
    public IOperationLogRepository OperationLogRepository { get; }
    public IConfigurationRepository ConfigurationRepository { get; }
    public HistorySampleQueue HistorySampleQueue { get; }
    public HistoryService HistoryService { get; }
    public QueryHistorySamplesUseCase QueryHistorySamplesUseCase { get; }
    public ExportHistoryCsvUseCase ExportHistoryCsvUseCase { get; }
    public HistoryRetentionService HistoryRetentionService { get; }
    public HistoryPersistWorker HistoryPersistWorker { get; }
    public AlarmPersistWorker AlarmPersistWorker { get; }
    public OperationLogService OperationLogService { get; }
    public OperationLogPersistWorker OperationLogPersistWorker { get; }
    public PersistenceRuntimeCoordinator PersistenceRuntime { get; }
    public ApplicationRuntimeHost ApplicationRuntime { get; }
    public AcquisitionRuntimeController AcquisitionRuntime { get; }
    public QueryOperationLogsUseCase QueryOperationLogsUseCase { get; }
    public QueryAlarmsUseCase QueryAlarmsUseCase { get; }
    public ConfigurationService ConfigurationService { get; }
    public SaveTagRuntimeSettingsUseCase SaveTagRuntimeSettingsUseCase { get; }
    public SaveRuntimeSettingsUseCase SaveRuntimeSettingsUseCase { get; }
    public MeasurementMapService MeasurementMapService { get; }
    public ProcessedFrameSnapshotStore ProcessedFrameStore { get; }
    public DashboardService DashboardService { get; }
    public UiSnapshotProvider UiSnapshotProvider { get; }
    public MonitoringRuntimeService RuntimeService { get; }
    public RuntimeLifecycleCoordinator RuntimeLifecycle { get; }
    public DataSourceHealthMonitor DataSourceHealthMonitor { get; }
}
