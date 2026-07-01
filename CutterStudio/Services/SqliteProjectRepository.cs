using CutterStudio.Models;
using Microsoft.Data.Sqlite;

namespace CutterStudio.Services;

/// <summary>
/// Small SQLite persistence layer. Each operation opens its own pooled connection,
/// avoiding a long-lived connection on the UI thread.
/// </summary>
public sealed class SqliteProjectRepository : IProjectRepository
{
    private readonly string _connectionString;

    public SqliteProjectRepository()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CutterStudio");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "projects.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                modified_utc TEXT NOT NULL,
                artwork_svg TEXT NOT NULL,
                settings_json TEXT NOT NULL DEFAULT '{}'
            );
            CREATE INDEX IF NOT EXISTS ix_projects_modified
                ON projects(modified_utc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> SaveAsync(ProjectRecord project, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (project.Id == 0)
        {
            command.CommandText =
                """
                INSERT INTO projects(name, created_utc, modified_utc, artwork_svg, settings_json)
                VALUES($name, $created, $modified, $artwork, $settings);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText =
                """
                UPDATE projects
                SET name = $name,
                    modified_utc = $modified,
                    artwork_svg = $artwork,
                    settings_json = $settings
                WHERE id = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", project.Id);
        }

        command.Parameters.AddWithValue("$name", project.Name.Trim());
        command.Parameters.AddWithValue("$created", project.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$modified", project.ModifiedUtc.ToString("O"));
        command.Parameters.AddWithValue("$artwork", project.ArtworkSvg);
        command.Parameters.AddWithValue("$settings", project.SettingsJson);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<ProjectRecord?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, created_utc, modified_utc, artwork_svg, settings_json
            FROM projects WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new ProjectRecord
        {
            Id = reader.GetInt64(0),
            Name = reader.GetString(1),
            CreatedUtc = DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
            ModifiedUtc = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
            ArtworkSvg = reader.GetString(4),
            SettingsJson = reader.GetString(5)
        };
    }

    public async Task<IReadOnlyList<RecentProject>> GetRecentAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var projects = new List<RecentProject>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, modified_utc
            FROM projects
            ORDER BY modified_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(new RecentProject(
                reader.GetInt64(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }

        return projects;
    }
}
