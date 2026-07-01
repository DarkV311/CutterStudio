using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CutterStudio.UpdatePublisher;

public sealed class PublisherForm : Form
{
    private readonly TextBox _ownerBox = new() { Text = "DarkV311" };
    private readonly TextBox _repoBox = new() { Text = "CutterStudio" };
    private readonly TextBox _tagBox = new() { Text = "v0.1" };
    private readonly TextBox _titleBox = new() { Text = "Cutter Studio v0.1" };
    private readonly TextBox _notesBox = new() { Text = "Cutter Studio update.", Multiline = true, Height = 70 };
    private readonly TextBox _assetBox = new() { Text = @"F:\Cutter\release\CutterStudio-win-x64-v0.1.zip" };
    private readonly TextBox _serverUrlBox = new() { Text = "http://69.169.109.119:5080" };
    private readonly TextBox _serverPasswordBox = new() { UseSystemPasswordChar = true };
    private readonly TextBox _logBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Button _publishButton = new() { Text = "Publish / Replace GitHub Release", Height = 38, Visible = false };
    private readonly Button _buildAndPublishButton = new() { Text = "Build + Publish GitHub", Height = 38, Visible = false };
    private readonly Button _buildAndServerPublishButton = new() { Text = "Build + Upload Update", Height = 52, BackColor = Color.FromArgb(48, 183, 163), ForeColor = Color.White };

    public PublisherForm()
    {
        Text = $"Cutter Studio Update Publisher v{AppVersion()}";
        Width = 860;
        Height = 650;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        _serverPasswordBox.Text = TryReadText(@"F:\Cutter\secrets\license-admin-password.txt");
        BuildUi();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(14), BackColor = Color.FromArgb(22, 25, 32) };
        header.Controls.Add(new Label
        {
            Text = $"Cutter Studio Update Publisher v{AppVersion()}",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(14, 17)
        });

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 330,
            Padding = new Padding(14),
            ColumnCount = 3
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        AddRow(form, 0, "Release tag", _tagBox);
        AddRow(form, 1, "Release title", _titleBox);
        AddRow(form, 2, "Notes", _notesBox);
        AddReadOnlyRow(form, 3, "Generated ZIP", _assetBox);
        AddRow(form, 4, "Server URL", _serverUrlBox);
        AddRow(form, 5, "Admin password", _serverPasswordBox);

        var browseButton = new Button { Text = "Browse" };
        browseButton.Visible = false;
        browseButton.Click += (_, _) => BrowseAsset();
        form.Controls.Add(browseButton, 2, 3);

        _publishButton.Click += async (_, _) => await PublishAsync();
        _tagBox.TextChanged += (_, _) => SyncVersionFieldsFromTag();
        form.Controls.Add(_publishButton, 1, 6);
        _buildAndPublishButton.Click += async (_, _) => await BuildAndPublishAsync();
        form.Controls.Add(_buildAndPublishButton, 2, 6);
        _buildAndServerPublishButton.Click += async (_, _) => await BuildAndPublishToServerAsync();
        form.Controls.Add(_buildAndServerPublishButton, 1, 7);
        form.SetColumnSpan(_buildAndServerPublishButton, 2);

        Controls.Add(_logBox);
        Controls.Add(form);
        Controls.Add(header);
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        }, 0, row);
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, 1, row);
        panel.SetColumnSpan(control, 2);
    }

    private static void AddReadOnlyRow(TableLayoutPanel panel, int row, string label, TextBox control)
    {
        control.ReadOnly = true;
        AddRow(panel, row, label, control);
    }

    private void BrowseAsset()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Release files (*.zip;*.exe;*.msi)|*.zip;*.exe;*.msi|All files (*.*)|*.*",
            InitialDirectory = @"F:\Cutter\release"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _assetBox.Text = dialog.FileName;
    }

    private async Task PublishAsync()
    {
        _publishButton.Enabled = false;
        _buildAndPublishButton.Enabled = false;
        _buildAndServerPublishButton.Enabled = false;
        try
        {
            var owner = Required(_ownerBox.Text, "GitHub owner");
            var repo = Required(_repoBox.Text, "Repository");
            var tag = NormalizeTag(Required(_tagBox.Text, "Release tag"));
            var assetPath = Required(_assetBox.Text, "Release file");
            if (!File.Exists(assetPath))
                throw new FileNotFoundException("Release file was not found.", assetPath);

            Log("Reading GitHub credential from Git Credential Manager...");
            var token = await GitCredentialReader.GetGitHubTokenAsync();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CutterStudio-UpdatePublisher");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            var release = await GetOrCreateReleaseAsync(client, owner, repo, tag);
            var assetName = Path.GetFileName(assetPath);
            await DeleteExistingAssetAsync(client, release, assetName);
            await UploadAssetAsync(client, owner, repo, release, assetPath, assetName);
            Log($"Done: https://github.com/{owner}/{repo}/releases/tag/{tag}");
            MessageBox.Show(this, "Update release published successfully.", "Update Publisher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Update Publisher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _publishButton.Enabled = true;
            _buildAndPublishButton.Enabled = true;
            _buildAndServerPublishButton.Enabled = true;
        }
    }

    private async Task BuildAndPublishAsync()
    {
        _publishButton.Enabled = false;
        _buildAndPublishButton.Enabled = false;
        _buildAndServerPublishButton.Enabled = false;
        try
        {
            var tag = NormalizeTag(Required(_tagBox.Text, "Release tag"));
            var version = tag[1..];
            Log($"Building client release {version}...");
            await RunPublishScriptAsync(version);
            _assetBox.Text = $@"F:\Cutter\release\CutterStudio-win-x64-v{version}.zip";
            Log("Build finished. Publishing to GitHub...");
            await PublishCoreAsync();
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Update Publisher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _publishButton.Enabled = true;
            _buildAndPublishButton.Enabled = true;
            _buildAndServerPublishButton.Enabled = true;
        }
    }

    private async Task BuildAndPublishToServerAsync()
    {
        _publishButton.Enabled = false;
        _buildAndPublishButton.Enabled = false;
        _buildAndServerPublishButton.Enabled = false;
        try
        {
            var tag = NormalizeTag(Required(_tagBox.Text, "Release tag"));
            var version = tag[1..];
            Log($"Building client release {version}...");
            await RunPublishScriptAsync(version);
            _assetBox.Text = $@"F:\Cutter\release\CutterStudio-win-x64-v{version}.zip";
            Log("Build finished. Uploading to license/update server...");
            await PublishToServerCoreAsync();
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Update Publisher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _publishButton.Enabled = true;
            _buildAndPublishButton.Enabled = true;
            _buildAndServerPublishButton.Enabled = true;
        }
    }

    private async Task RunPublishScriptAsync(string version)
    {
        var root = FindProjectRoot();
        var script = Path.Combine(root, "PublishRelease.ps1");
        if (!File.Exists(script))
            throw new FileNotFoundException("PublishRelease.ps1 was not found.", script);

        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -File \"{script}\" -Version {version} -ClientOnly",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start publish script.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (!string.IsNullOrWhiteSpace(output))
            Log(output.Trim());
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Build failed." : error.Trim());
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PublishRelease.ps1")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return @"F:\Cutter";
    }

    private async Task PublishCoreAsync()
    {
        var owner = Required(_ownerBox.Text, "GitHub owner");
        var repo = Required(_repoBox.Text, "Repository");
        var tag = NormalizeTag(Required(_tagBox.Text, "Release tag"));
        var assetPath = Required(_assetBox.Text, "Release file");
        if (!File.Exists(assetPath))
            throw new FileNotFoundException("Release file was not found.", assetPath);

        Log("Reading GitHub credential from Git Credential Manager...");
        var token = await GitCredentialReader.GetGitHubTokenAsync();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CutterStudio-UpdatePublisher");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        var release = await GetOrCreateReleaseAsync(client, owner, repo, tag);
        var assetName = Path.GetFileName(assetPath);
        await DeleteExistingAssetAsync(client, release, assetName);
        await UploadAssetAsync(client, owner, repo, release, assetPath, assetName);
        Log($"Done: https://github.com/{owner}/{repo}/releases/tag/{tag}");
        MessageBox.Show(this, "Update release published successfully.", "Update Publisher", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task PublishToServerCoreAsync()
    {
        var serverUrl = Required(_serverUrlBox.Text, "Server URL").TrimEnd('/');
        var adminPassword = Required(_serverPasswordBox.Text, "Admin password");
        var tag = NormalizeTag(Required(_tagBox.Text, "Release tag"));
        var version = tag[1..];
        var assetPath = Required(_assetBox.Text, "Release file");
        if (!File.Exists(assetPath))
            throw new FileNotFoundException("Release file was not found.", assetPath);

        using var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            UseCookies = true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };

        Log("Logging in to update server...");
        using (var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = adminPassword
        }))
        {
            var login = await client.PostAsync($"{serverUrl}/admin/login", loginContent);
            login.EnsureSuccessStatusCode();
        }

        Log("Uploading release ZIP to server...");
        await using var stream = File.OpenRead(assetPath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(version), "version" },
            { new StringContent("stable"), "channel" },
            { new StringContent(_notesBox.Text), "notes" },
            { new StringContent("on"), "published" },
            { fileContent, "releaseFile", Path.GetFileName(assetPath) }
        };

        var response = await client.PostAsync($"{serverUrl}/admin/releases/create", form);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Server upload failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        var latest = await client.GetFromJsonAsync<LatestReleaseResponse>($"{serverUrl}/api/releases/latest?channel=stable", JsonOptions());
        if (latest is null || !latest.Available || !latest.Version.Equals(version, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Upload finished, but the server latest version is not {version}. Check the admin panel.");

        Log($"Done. Server latest version is {latest.Version}.");
        Log($"Download URL: {serverUrl}{latest.DownloadUrl}");
        MessageBox.Show(this, $"Update v{latest.Version} uploaded successfully.\n\nClients will see it from the server.", "Update Publisher", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private sealed record LatestReleaseResponse(
        bool Available,
        string Version,
        string Channel,
        string DownloadUrl,
        string Sha256,
        string Notes,
        DateTime CreatedUtc);

    private async Task<GitHubRelease> GetOrCreateReleaseAsync(HttpClient client, string owner, string repo, string tag)
    {
        var get = await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/releases/tags/{tag}");
        if (get.IsSuccessStatusCode)
        {
            Log("Release already exists. Replacing asset if needed...");
            return (await JsonSerializer.DeserializeAsync<GitHubRelease>(
                await get.Content.ReadAsStreamAsync(),
                JsonOptions()))!;
        }

        Log("Creating release...");
        var body = JsonSerializer.Serialize(new
        {
            tag_name = tag,
            target_commitish = "main",
            name = string.IsNullOrWhiteSpace(_titleBox.Text) ? tag : _titleBox.Text.Trim(),
            body = _notesBox.Text,
            draft = false,
            prerelease = false
        });
        var response = await client.PostAsync(
            $"https://api.github.com/repos/{owner}/{repo}/releases",
            new StringContent(body, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        return (await JsonSerializer.DeserializeAsync<GitHubRelease>(
            await response.Content.ReadAsStreamAsync(),
            JsonOptions()))!;
    }

    private async Task DeleteExistingAssetAsync(HttpClient client, GitHubRelease release, string assetName)
    {
        foreach (var asset in release.Assets ?? Array.Empty<GitHubAsset>())
        {
            if (!asset.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                continue;
            Log("Deleting existing asset...");
            var response = await client.DeleteAsync(asset.Url);
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task UploadAssetAsync(
        HttpClient client,
        string owner,
        string repo,
        GitHubRelease release,
        string path,
        string assetName)
    {
        Log("Uploading asset...");
        var uploadBase = !string.IsNullOrWhiteSpace(release.UploadUrl)
            ? release.UploadUrl.Split('{')[0]
            : $"https://uploads.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/{release.Id}/assets";
        var uploadUrl = uploadBase + "?name=" + Uri.EscapeDataString(assetName);
        await using var stream = File.OpenRead(path);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? "application/zip"
            : "application/octet-stream");
        var response = await client.PostAsync(uploadUrl, content);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"GitHub upload failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }
    }

    private void SyncVersionFieldsFromTag()
    {
        var tag = NormalizeTag(_tagBox.Text);
        if (string.IsNullOrWhiteSpace(tag) || tag == "v")
            return;
        _titleBox.Text = $"Cutter Studio {tag}";
        _assetBox.Text = $@"F:\Cutter\release\CutterStudio-win-x64-{tag}.zip";
    }

    private static string NormalizeTag(string value)
    {
        var tag = value.Trim();
        return tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? "v" + tag[1..] : "v" + tag;
    }

    private static string AppVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.1";

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);

    private static string Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
        return value.Trim();
    }

    private void Log(string text) => _logBox.AppendText($"[{DateTime.Now:T}] {text}{Environment.NewLine}");

    private static string TryReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("upload_url")] string? UploadUrl,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("url")] string Url);
}
