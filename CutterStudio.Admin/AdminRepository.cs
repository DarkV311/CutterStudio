using Microsoft.Data.Sqlite;

namespace CutterStudio.Admin;

public sealed class AdminRepository
{
    private readonly string _connectionString;

    public AdminRepository(IWebHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "admin.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS licenses(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                license_key TEXT NOT NULL UNIQUE,
                customer_name TEXT NOT NULL,
                customer_email TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                expires_utc TEXT NULL,
                max_activations INTEGER NOT NULL,
                is_blocked INTEGER NOT NULL,
                notes TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS activations(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                license_id INTEGER NOT NULL,
                machine_id TEXT NOT NULL,
                app_version TEXT NOT NULL,
                activated_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL,
                UNIQUE(license_id, machine_id),
                FOREIGN KEY(license_id) REFERENCES licenses(id)
            );

            CREATE TABLE IF NOT EXISTS releases(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                version TEXT NOT NULL,
                channel TEXT NOT NULL,
                file_name TEXT NOT NULL,
                download_url TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                notes TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                is_published INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_releases_channel_published ON releases(channel, is_published, created_utc DESC);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<LicenseRecord>> GetLicensesAsync()
    {
        var results = new List<LicenseRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, license_key, customer_name, customer_email, created_utc, expires_utc,
                   max_activations, is_blocked, notes
            FROM licenses
            ORDER BY created_utc DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(ReadLicense(reader));
        return results;
    }

    public async Task<IReadOnlyList<ReleaseRecord>> GetReleasesAsync()
    {
        var results = new List<ReleaseRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, version, channel, file_name, download_url, sha256, notes, created_utc, is_published
            FROM releases
            ORDER BY created_utc DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(ReadRelease(reader));
        return results;
    }

    public async Task<LicenseRecord> CreateLicenseAsync(
        string customerName,
        string customerEmail,
        DateTime? expiresUtc,
        int maxActivations,
        string notes)
    {
        var key = GenerateLicenseKey();
        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO licenses(license_key, customer_name, customer_email, created_utc, expires_utc,
                                 max_activations, is_blocked, notes)
            VALUES($key, $name, $email, $created, $expires, $max, 0, $notes);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$name", customerName.Trim());
        command.Parameters.AddWithValue("$email", customerEmail.Trim());
        command.Parameters.AddWithValue("$created", now.ToString("O"));
        command.Parameters.AddWithValue("$expires", expiresUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$max", Math.Clamp(maxActivations, 1, 1000));
        command.Parameters.AddWithValue("$notes", notes.Trim());
        var id = Convert.ToInt64(await command.ExecuteScalarAsync());
        return new LicenseRecord(id, key, customerName, customerEmail, now, expiresUtc, maxActivations, false, notes);
    }

    public async Task SetLicenseBlockedAsync(long id, bool blocked)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE licenses SET is_blocked = $blocked WHERE id = $id;";
        command.Parameters.AddWithValue("$blocked", blocked ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<ReleaseRecord> CreateReleaseAsync(
        string version,
        string channel,
        string fileName,
        string downloadUrl,
        string sha256,
        string notes,
        bool published)
    {
        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO releases(version, channel, file_name, download_url, sha256, notes, created_utc, is_published)
            VALUES($version, $channel, $file, $url, $sha, $notes, $created, $published);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$version", version.Trim());
        command.Parameters.AddWithValue("$channel", string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim());
        command.Parameters.AddWithValue("$file", fileName);
        command.Parameters.AddWithValue("$url", downloadUrl);
        command.Parameters.AddWithValue("$sha", sha256);
        command.Parameters.AddWithValue("$notes", notes.Trim());
        command.Parameters.AddWithValue("$created", now.ToString("O"));
        command.Parameters.AddWithValue("$published", published ? 1 : 0);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync());
        return new ReleaseRecord(id, version, channel, fileName, downloadUrl, sha256, notes, now, published);
    }

    public async Task<LatestReleaseResponse> GetLatestReleaseAsync(string channel)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, version, channel, file_name, download_url, sha256, notes, created_utc, is_published
            FROM releases
            WHERE channel = $channel AND is_published = 1
            ORDER BY created_utc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$channel", string.IsNullOrWhiteSpace(channel) ? "stable" : channel);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new LatestReleaseResponse(false, "", channel, "", "", "", DateTime.MinValue);
        var release = ReadRelease(reader);
        return new LatestReleaseResponse(true, release.Version, release.Channel, release.DownloadUrl,
            release.Sha256, release.Notes, release.CreatedUtc);
    }

    public async Task<LicenseActivationResponse> ActivateAsync(LicenseActivationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.MachineId))
            return new LicenseActivationResponse(false, "invalid_request", null, 0, 0, "License key and machine id are required.");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var license = await FindLicenseAsync(connection, request.LicenseKey.Trim());
        if (license is null)
            return new LicenseActivationResponse(false, "not_found", null, 0, 0, "License key was not found.");
        if (license.IsBlocked)
            return new LicenseActivationResponse(false, "blocked", license.ExpiresUtc, 0, license.MaxActivations, "License is blocked.");
        if (license.ExpiresUtc is not null && license.ExpiresUtc.Value < DateTime.UtcNow)
            return new LicenseActivationResponse(false, "expired", license.ExpiresUtc, 0, license.MaxActivations, "License is expired.");

        var used = await CountActivationsAsync(connection, license.Id);
        var exists = await ActivationExistsAsync(connection, license.Id, request.MachineId);
        if (!exists && used >= license.MaxActivations)
            return new LicenseActivationResponse(false, "activation_limit", license.ExpiresUtc, used, license.MaxActivations, "Activation limit reached.");

        var now = DateTime.UtcNow;
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO activations(license_id, machine_id, app_version, activated_utc, last_seen_utc)
            VALUES($license, $machine, $version, $activated, $seen)
            ON CONFLICT(license_id, machine_id)
            DO UPDATE SET app_version = excluded.app_version, last_seen_utc = excluded.last_seen_utc;
            """;
        command.Parameters.AddWithValue("$license", license.Id);
        command.Parameters.AddWithValue("$machine", request.MachineId.Trim());
        command.Parameters.AddWithValue("$version", request.AppVersion.Trim());
        command.Parameters.AddWithValue("$activated", now.ToString("O"));
        command.Parameters.AddWithValue("$seen", now.ToString("O"));
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();

        used = await CountActivationsAsync(connection, license.Id);
        return new LicenseActivationResponse(true, "active", license.ExpiresUtc, used, license.MaxActivations, "License is active.");
    }

    private async Task<LicenseRecord?> FindLicenseAsync(SqliteConnection connection, string key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, license_key, customer_name, customer_email, created_utc, expires_utc,
                   max_activations, is_blocked, notes
            FROM licenses WHERE license_key = $key;
            """;
        command.Parameters.AddWithValue("$key", key);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadLicense(reader) : null;
    }

    private static async Task<int> CountActivationsAsync(SqliteConnection connection, long licenseId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM activations WHERE license_id = $id;";
        command.Parameters.AddWithValue("$id", licenseId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> ActivationExistsAsync(SqliteConnection connection, long licenseId, string machineId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM activations WHERE license_id = $id AND machine_id = $machine;";
        command.Parameters.AddWithValue("$id", licenseId);
        command.Parameters.AddWithValue("$machine", machineId.Trim());
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static LicenseRecord ReadLicense(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
            reader.GetInt32(6),
            reader.GetInt32(7) == 1,
            reader.GetString(8));

    private static ReleaseRecord ReadRelease(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind),
            reader.GetInt32(8) == 1);

    private static string GenerateLicenseKey()
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var token = Convert.ToHexString(bytes);
        return $"CUT-{token[..4]}-{token[4..8]}-{token[8..12]}-{token[12..16]}-{token[16..24]}";
    }
}
