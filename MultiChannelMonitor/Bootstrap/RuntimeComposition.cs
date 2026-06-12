using Application.Abstractions.DataSource;
using Application.Abstractions.Events;
using Application.Abstractions.Persistence;
using Application.Abstractions.Time;
using Application.BackgroundWorkers;
using Application.Caches;
using Application.Configuration;
using Application.Pipelines;
using Application.Queues;
using Application.Services;
using Application.UseCases.Alarms;
using Application.UseCases.Export;
using Application.UseCases.History;
using Application.UseCases.Logs;
using Application.UseCases.Settings;
using Domain.Tags;
using Infrastructure.Export;
using Infrastructure.Persistence;

namespace Presentation.Wpf.Bootstrap;

/// <summary>
/// 应用组合根。这里只负责对象创建、依赖连接和启动前初始化，不承载业务逻辑。
/// </summary>
public sealed class RuntimeComposition
{
    private RuntimeComposition(
        CoreServices core,
        PersistenceFoundation persistence,
        ApplicationServices application,
        UseCaseServices useCases,
        EventPipelineRegistration eventPipeline,
        RuntimeServices runtime)
    {
        RuntimeOptionsStore = core.RuntimeOptionsStore;
        TagRuntimeConfigurationStore = core.TagRuntimeConfigurationStore;
        TrendDiagnosisOptions = core.TrendDiagnosisOptions;
        Clock = core.Clock;
        TagDefinitions = core.TagDefinitions;
        DefinitionMap = core.DefinitionMap;

        AlarmEventQueue = persistence.AlarmEventQueue;
        OperationLogQueue = persistence.OperationLogQueue;
        HistorySampleQueue = persistence.HistorySampleQueue;
        DatabaseConnectionFactory = persistence.DatabaseConnectionFactory;
        HistoryRepository = persistence.HistoryRepository;
        AlarmRepository = persistence.AlarmRepository;
        OperationLogRepository = persistence.OperationLogRepository;
        ConfigurationRepository = persistence.ConfigurationRepository;
        OperationLogService = persistence.OperationLogService;
        ConfigurationService = persistence.ConfigurationService;

        TagService = application.TagService;
        ChartDataService = application.ChartDataService;
        AlarmService = application.AlarmService;
        HistoryService = application.HistoryService;
        HistoryRetentionService = application.HistoryRetentionService;
        MeasurementMapService = application.MeasurementMapService;
        ProcessedFrameStore = application.ProcessedFrameStore;
        DashboardService = application.DashboardService;
        UiSnapshotProvider = application.UiSnapshotProvider;

        QueryHistorySamplesUseCase = useCases.QueryHistorySamplesUseCase;
        ExportHistoryCsvUseCase = useCases.ExportHistoryCsvUseCase;
        QueryOperationLogsUseCase = useCases.QueryOperationLogsUseCase;
        QueryAlarmsUseCase = useCases.QueryAlarmsUseCase;
        SaveTagRuntimeSettingsUseCase = useCases.SaveTagRuntimeSettingsUseCase;
        SaveRuntimeSettingsUseCase = useCases.SaveRuntimeSettingsUseCase;
        AcknowledgeAlarmUseCase = eventPipeline.AcknowledgeAlarmUseCase;

        EventPublisher = eventPipeline.EventPublisher;
        HistoryPersistWorker = runtime.HistoryPersistWorker;
        AlarmPersistWorker = runtime.AlarmPersistWorker;
        OperationLogPersistWorker = runtime.OperationLogPersistWorker;
        PersistenceRuntime = runtime.PersistenceRuntime;
        ApplicationRuntime = runtime.ApplicationRuntime;
        RuntimeService = runtime.RuntimeService;
        RuntimeLifecycle = runtime.RuntimeLifecycle;
        AcquisitionRuntime = runtime.AcquisitionRuntime;
        DataSourceHealthMonitor = runtime.DataSourceHealthMonitor;
    }

    /// <summary>
    /// 按固定顺序完成异步初始化并创建完整对象图。
    /// </summary>
    public static async Task<RuntimeComposition> CreateAsync(
        RuntimeCompositionDependencies? dependencies = null,
        CancellationToken cancellationToken = default)
    {
        dependencies ??= RuntimeCompositionDependencies.CreateDefault();

        // 第一阶段只创建无 I/O 的核心对象，确保默认配置和标签定义先就位。
        var core = CreateCoreServices(dependencies.Clock);
        var persistence = CreatePersistenceFoundation(core, dependencies);

        // 数据库迁移和配置加载必须先完成；后续对象的容量、批次和周期都依赖加载后的配置。
        await dependencies.InitializePersistenceAsync(cancellationToken).ConfigureAwait(false);
        await persistence.ConfigurationService.LoadAsync(cancellationToken).ConfigureAwait(false);

        var application = CreateApplicationServices(core, persistence);

        // 恢复未关闭告警后再开放事件管道，避免启动期间产生重复告警事件。
        var openAlarms = await persistence.AlarmRepository
            .QueryOpenAlarmsAsync(cancellationToken)
            .ConfigureAwait(false);
        application.AlarmService.RestoreEvents(openAlarms);

        var eventPipeline = EventRegistration.Create(
            core.Clock,
            core.TagRuntimeConfigurationStore,
            persistence.AlarmEventQueue,
            persistence.OperationLogService,
            application.TagService,
            application.AlarmService,
            application.HistoryService,
            application.MeasurementMapService);
        var runtime = CreateRuntimeServices(
            core,
            persistence,
            application,
            eventPipeline,
            dependencies.DataSourceFactory);
        var useCases = CreateUseCases(core, persistence, application, runtime);

        return new RuntimeComposition(
            core,
            persistence,
            application,
            useCases,
            eventPipeline,
            runtime);
    }

    private static CoreServices CreateCoreServices(IClock clock)
    {
        var runtimeOptionsStore = new RuntimeOptionsStore(new MonitorRuntimeOptions());
        var tagDefinitions = TagDefinitionCatalog.CreateDefaults();
        return new CoreServices(
            runtimeOptionsStore,
            new TagRuntimeConfigurationStore(
                tagDefinitions.Select(TagRuntimeConfiguration.FromDefinition)),
            new TrendDiagnosisOptions(),
            clock,
            tagDefinitions,
            tagDefinitions.ToDictionary(item => item.TagId, StringComparer.Ordinal));
    }

    private static PersistenceFoundation CreatePersistenceFoundation(
        CoreServices core,
        RuntimeCompositionDependencies dependencies)
    {
        var alarmEventQueue = new AlarmEventQueue();
        var operationLogQueue = new OperationLogQueue();
        var historySampleQueue = new HistorySampleQueue();
        var operationLogService = new OperationLogService(
            dependencies.OperationLogRepository,
            operationLogQueue,
            core.Clock);
        var configurationService = new ConfigurationService(
            core.TagDefinitions,
            dependencies.ConfigurationRepository,
            core.TagRuntimeConfigurationStore,
            core.RuntimeOptionsStore,
            operationLogService);

        return new PersistenceFoundation(
            alarmEventQueue,
            operationLogQueue,
            historySampleQueue,
            dependencies.DatabaseConnectionFactory,
            dependencies.HistoryRepository,
            dependencies.AlarmRepository,
            dependencies.OperationLogRepository,
            dependencies.ConfigurationRepository,
            operationLogService,
            configurationService);
    }

    private static ApplicationServices CreateApplicationServices(
        CoreServices core,
        PersistenceFoundation persistence)
    {
        var tagService = new TagService(new TagCache(core.Options.TrendBufferCapacity), core.Clock);
        var chartDataService = new ChartDataService(
            tagService,
            core.DefinitionMap,
            core.TagRuntimeConfigurationStore,
            core.TrendDiagnosisOptions);
        var alarmService = new AlarmService(core.TagDefinitions, core.TagRuntimeConfigurationStore);
        var historyService = new HistoryService(
            persistence.HistoryRepository,
            persistence.HistorySampleQueue,
            persistence.OperationLogService);
        var historyRetentionService = new HistoryRetentionService(
            persistence.HistoryRepository,
            persistence.OperationLogService,
            core.Clock,
            core.Options.HistoryRetentionDays,
            core.Options.HistoryRetentionDeleteBatchSize);
        var measurementMapService = new MeasurementMapService(new MatrixFrameCache());
        var processedFrameStore = new ProcessedFrameSnapshotStore();
        var dashboardService = new DashboardService(tagService, alarmService, core.Clock);
        var uiSnapshotProvider = new UiSnapshotProvider(
            tagService,
            alarmService,
            dashboardService,
            chartDataService,
            measurementMapService,
            core.Clock,
            processedFrameStore);

        return new ApplicationServices(
            tagService,
            chartDataService,
            alarmService,
            historyService,
            historyRetentionService,
            measurementMapService,
            processedFrameStore,
            dashboardService,
            uiSnapshotProvider);
    }

    private static UseCaseServices CreateUseCases(
        CoreServices core,
        PersistenceFoundation persistence,
        ApplicationServices application,
        RuntimeServices runtime)
    {
        return new UseCaseServices(
            new QueryHistorySamplesUseCase(
                application.HistoryService,
                core.TagRuntimeConfigurationStore),
            new ExportHistoryCsvUseCase(
                new SimpleCsvExporter(),
                persistence.HistoryRepository,
                persistence.OperationLogService),
            new QueryOperationLogsUseCase(
                persistence.OperationLogService,
                runtime.PersistenceRuntime),
            new QueryAlarmsUseCase(
                persistence.AlarmRepository,
                persistence.OperationLogService),
            new SaveTagRuntimeSettingsUseCase(persistence.ConfigurationService),
            new SaveRuntimeSettingsUseCase(persistence.ConfigurationService));
    }

    private static RuntimeServices CreateRuntimeServices(
        CoreServices core,
        PersistenceFoundation persistence,
        ApplicationServices application,
        EventPipelineRegistration eventPipeline,
        Func<IRuntimeOptionsStore, IClock, IDataSource> dataSourceFactory)
    {
        var historyPersistWorker = new HistoryPersistWorker(
            persistence.HistorySampleQueue,
            persistence.HistoryRepository,
            core.Options.HistoryBatchInterval,
            core.Options.HistoryMaxBatchSize);
        var alarmPersistWorker = new AlarmPersistWorker(
            persistence.AlarmEventQueue,
            persistence.AlarmRepository,
            core.Options.AlarmBatchInterval,
            core.Options.AlarmMaxBatchSize);
        var operationLogPersistWorker = new OperationLogPersistWorker(
            persistence.OperationLogQueue,
            persistence.OperationLogRepository,
            core.Options.OperationLogBatchInterval,
            core.Options.OperationLogMaxBatchSize);
        var persistenceRuntime = new PersistenceRuntimeCoordinator(
            historyPersistWorker,
            alarmPersistWorker,
            operationLogPersistWorker);
        var applicationRuntime = new ApplicationRuntimeHost(
            persistenceRuntime,
            persistence.OperationLogService,
            application.HistoryRetentionService);
        var dataSourceHealthMonitor = new DataSourceHealthMonitor(core.Clock);
        var runtimeService = new MonitoringRuntimeService(
            new DataSourceService(dataSourceFactory(core.RuntimeOptionsStore, core.Clock)),
            new DataCleanPipeline(core.TagDefinitions),
            application.AlarmService,
            eventPipeline.EventPublisher,
            core.Clock,
            dataSourceHealthMonitor,
            core.RuntimeOptionsStore,
            application.ProcessedFrameStore,
            application.MeasurementMapService);
        var runtimeLifecycle = new RuntimeLifecycleCoordinator(runtimeService.RunAsync);

        return new RuntimeServices(
            historyPersistWorker,
            alarmPersistWorker,
            operationLogPersistWorker,
            persistenceRuntime,
            applicationRuntime,
            runtimeService,
            runtimeLifecycle,
            new AcquisitionRuntimeController(
                runtimeLifecycle,
                persistenceRuntime,
                persistence.OperationLogService),
            dataSourceHealthMonitor);
    }

    public MonitorRuntimeOptions Options => RuntimeOptionsStore.Snapshot;
    public RuntimeOptionsStore RuntimeOptionsStore { get; }
    public TagRuntimeConfigurationStore TagRuntimeConfigurationStore { get; }
    public TrendDiagnosisOptions TrendDiagnosisOptions { get; }
    public IClock Clock { get; }
    public IReadOnlyList<TagDefinition> TagDefinitions { get; }
    public IReadOnlyDictionary<string, TagDefinition> DefinitionMap { get; }
    public TagService TagService { get; }
    public ChartDataService ChartDataService { get; }
    public AlarmService AlarmService { get; }
    public AlarmEventQueue AlarmEventQueue { get; }
    public OperationLogQueue OperationLogQueue { get; }
    public AcknowledgeAlarmUseCase AcknowledgeAlarmUseCase { get; }
    public SqliteConnectionFactory? DatabaseConnectionFactory { get; }
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
    public IApplicationEventPublisher EventPublisher { get; }
    public MonitoringRuntimeService RuntimeService { get; }
    public RuntimeLifecycleCoordinator RuntimeLifecycle { get; }
    public DataSourceHealthMonitor DataSourceHealthMonitor { get; }

    private sealed record CoreServices(
        RuntimeOptionsStore RuntimeOptionsStore,
        TagRuntimeConfigurationStore TagRuntimeConfigurationStore,
        TrendDiagnosisOptions TrendDiagnosisOptions,
        IClock Clock,
        IReadOnlyList<TagDefinition> TagDefinitions,
        IReadOnlyDictionary<string, TagDefinition> DefinitionMap)
    {
        public MonitorRuntimeOptions Options => RuntimeOptionsStore.Snapshot;
    }

    private sealed record PersistenceFoundation(
        AlarmEventQueue AlarmEventQueue,
        OperationLogQueue OperationLogQueue,
        HistorySampleQueue HistorySampleQueue,
        SqliteConnectionFactory? DatabaseConnectionFactory,
        IHistoryRepository HistoryRepository,
        IAlarmRepository AlarmRepository,
        IOperationLogRepository OperationLogRepository,
        IConfigurationRepository ConfigurationRepository,
        OperationLogService OperationLogService,
        ConfigurationService ConfigurationService);

    private sealed record ApplicationServices(
        TagService TagService,
        ChartDataService ChartDataService,
        AlarmService AlarmService,
        HistoryService HistoryService,
        HistoryRetentionService HistoryRetentionService,
        MeasurementMapService MeasurementMapService,
        ProcessedFrameSnapshotStore ProcessedFrameStore,
        DashboardService DashboardService,
        UiSnapshotProvider UiSnapshotProvider);

    private sealed record UseCaseServices(
        QueryHistorySamplesUseCase QueryHistorySamplesUseCase,
        ExportHistoryCsvUseCase ExportHistoryCsvUseCase,
        QueryOperationLogsUseCase QueryOperationLogsUseCase,
        QueryAlarmsUseCase QueryAlarmsUseCase,
        SaveTagRuntimeSettingsUseCase SaveTagRuntimeSettingsUseCase,
        SaveRuntimeSettingsUseCase SaveRuntimeSettingsUseCase);

    private sealed record RuntimeServices(
        HistoryPersistWorker HistoryPersistWorker,
        AlarmPersistWorker AlarmPersistWorker,
        OperationLogPersistWorker OperationLogPersistWorker,
        PersistenceRuntimeCoordinator PersistenceRuntime,
        ApplicationRuntimeHost ApplicationRuntime,
        MonitoringRuntimeService RuntimeService,
        RuntimeLifecycleCoordinator RuntimeLifecycle,
        AcquisitionRuntimeController AcquisitionRuntime,
        DataSourceHealthMonitor DataSourceHealthMonitor);
}
