namespace Infrastructure.Persistence;

internal sealed record SqliteSchemaMigration(
    int Version,
    string Sql);
