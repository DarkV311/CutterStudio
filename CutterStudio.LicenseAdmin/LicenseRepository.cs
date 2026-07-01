using System.Net.Http.Json;
using System.Text.Json;

namespace CutterStudio.LicenseAdmin;

public sealed class LicenseRepository
{
    private readonly HttpClient _client = new();
    private readonly string _serverUrl;
    private readonly string _adminPassword;

    public LicenseRepository(string appDirectory)
    {
        var settings = LoadSettings(appDirectory);
        _serverUrl = settings.ServerUrl.TrimEnd('/');
        _adminPassword = settings.AdminPassword;
        _client.Timeout = TimeSpan.FromSeconds(30);
        _client.DefaultRequestHeaders.Remove("X-Admin-Password");
        _client.DefaultRequestHeaders.Add("X-Admin-Password", _adminPassword);
    }

    public string ServerUrl => _serverUrl;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task<IReadOnlyList<LicenseRecord>> GetLicensesAsync()
    {
        using var response = await _client.GetAsync($"{_serverUrl}/api/admin/licenses");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<LicenseRecord>>(JsonOptions())
               ?? Array.Empty<LicenseRecord>();
    }

    public async Task<LicenseRecord> CreateLicenseAsync(
        string customerName,
        string customerEmail,
        int durationDays,
        int maxActivations,
        string notes)
    {
        using var response = await _client.PostAsJsonAsync($"{_serverUrl}/api/admin/licenses/create", new
        {
            customerName,
            customerEmail,
            durationDays,
            expiresUtc = (DateTime?)null,
            maxActivations,
            notes
        }, JsonOptions());
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<LicenseRecord>(JsonOptions())
               ?? throw new InvalidOperationException("Server returned an empty license response.");
    }

    public async Task SetLicenseBlockedAsync(long id, bool blocked)
    {
        using var response = await _client.PostAsJsonAsync($"{_serverUrl}/api/admin/licenses/block", new
        {
            id,
            blocked
        }, JsonOptions());
        await EnsureSuccessAsync(response);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Admin password is invalid for the license server.");

        throw new InvalidOperationException($"License server request failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
    }

    private static RepositorySettings LoadSettings(string appDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(appDirectory, "license-admin.json"),
            @"F:\Cutter\secrets\license-admin.json"
        };

        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                var settings = JsonSerializer.Deserialize<RepositorySettings>(File.ReadAllText(path), JsonOptions());
                if (settings is not null && !string.IsNullOrWhiteSpace(settings.ServerUrl) && !string.IsNullOrWhiteSpace(settings.AdminPassword))
                    return settings;
            }
            catch
            {
                // A malformed config file should not make the desktop admin disappear.
                // Fall back to the local secret file below.
            }
        }

        var passwordFile = @"F:\Cutter\secrets\license-admin-password.txt";
        var password = File.Exists(passwordFile) ? File.ReadAllText(passwordFile).Trim() : "";
        return new RepositorySettings("http://69.169.109.119:5080", password);
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed record RepositorySettings(string ServerUrl, string AdminPassword);
}
