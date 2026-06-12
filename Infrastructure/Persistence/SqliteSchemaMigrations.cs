namespace Infrastructure.Persistence;

internal static class SqliteSchemaMigrations
{
    public static IReadOnlyList<SqliteSchemaMigration> All { get; } =
    [
        new(
            1,
            """
            CREATE TABLE IF NOT EXISTS history_samples (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag_id TEXT NOT NULL,
                value REAL NOT NULL,
                timestamp_utc_ticks INTEGER NOT NULL,
                quality INTEGER NOT NULL,
                alarm_state INTEGER NOT NULL,
                source TEXT NOT NULL,
                sequence_no INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_history_samples_tag_time
            ON history_samples(tag_id, timestamp_utc_ticks);

            CREATE TABLE IF NOT EXISTS alarm_events (
                alarm_id TEXT PRIMARY KEY,
                tag_id TEXT NOT NULL,
                level INTEGER NOT NULL,
                state INTEGER NOT NULL,
                trigger_value REAL NOT NULL,
                trigger_time_utc_ticks INTEGER NOT NULL,
                message TEXT NOT NULL,
                acknowledge_time_utc_ticks INTEGER NULL,
                recover_time_utc_ticks INTEGER NULL
            );

            CREATE INDEX IF NOT EXISTS idx_alarm_events_trigger_time
            ON alarm_events(trigger_time_utc_ticks DESC);

            CREATE INDEX IF NOT EXISTS idx_alarm_events_tag_time
            ON alarm_events(tag_id, trigger_time_utc_ticks DESC);
            """),
        new(
            2,
            """
            CREATE TABLE operation_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp_utc_ticks INTEGER NOT NULL,
                level INTEGER NOT NULL,
                category TEXT NOT NULL,
                action TEXT NOT NULL,
                source TEXT NOT NULL,
                message TEXT NOT NULL,
                detail TEXT NULL,
                correlation_id TEXT NULL
            );

            CREATE INDEX idx_operation_logs_time
            ON operation_logs(timestamp_utc_ticks DESC);

            CREATE INDEX idx_operation_logs_category_time
            ON operation_logs(category, timestamp_utc_ticks DESC);
            """),
        new(
            3,
            """
            CREATE TABLE tag_runtime_settings (
                tag_id TEXT PRIMARY KEY,
                alarm_enabled INTEGER NOT NULL,
                warning_low REAL NULL,
                alarm_low REAL NULL,
                warning_high REAL NULL,
                alarm_high REAL NULL,
                is_historized INTEGER NOT NULL,
                history_interval_ms INTEGER NOT NULL
            );

            CREATE TABLE runtime_settings (
                setting_key TEXT PRIMARY KEY,
                setting_value TEXT NOT NULL
            );
            """),
        new(
            4,
            """
            DELETE FROM history_samples
            WHERE id NOT IN (
                SELECT MIN(id)
                FROM history_samples
                GROUP BY tag_id, source
            );

            CREATE UNIQUE INDEX idx_history_samples_tag_source
            ON history_samples(tag_id, source);
            """),
        new(
            5,
            """
            ALTER TABLE alarm_events ADD COLUMN alarm_type INTEGER NOT NULL DEFAULT 5;
            ALTER TABLE alarm_events ADD COLUMN last_updated_time_utc_ticks INTEGER NULL;
            ALTER TABLE alarm_events ADD COLUMN close_reason TEXT NULL;

            UPDATE alarm_events
            SET last_updated_time_utc_ticks = COALESCE(
                recover_time_utc_ticks,
                acknowledge_time_utc_ticks,
                trigger_time_utc_ticks)
            WHERE last_updated_time_utc_ticks IS NULL;

            CREATE INDEX idx_alarm_events_state_time
            ON alarm_events(state, trigger_time_utc_ticks DESC);
            """)
    ];

    public static int CurrentVersion => All[^1].Version;
}
