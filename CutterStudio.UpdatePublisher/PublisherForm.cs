using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CutterStudio.UpdatePublisher;

public sealed class PublisherForm : Form
{
    private readonly TextBox _ownerBox = new() { Text = "DarkV311" };
    private readonly TextBox _repoBox = new() { Text = "CutterStudio" };
    private readonly TextBox _tagBox = new() { Text = "v1.0.2" };
    private readonly TextBox _titleBox = new() { Text = "Cutter Studio v1.0.2" };
    private readonly TextBox _notesBox = new() { Text = "Cutter Studio update.", Multiline = true, Height = 70 };
    private readonly TextBox _assetBox = new() { Text = @"F:\Cutter\release\CutterStudio-win-x64-v1.0.2.zip" };
    private readonly TextBox _logBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Button _publishButton = new() { Text = "Publish / Replace GitHub Release", Height = 38 };

    public PublisherForm()
    {
        Text = "Cutter Studio Update Publisher";
        Width = 860;
        Height = 650;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        BuildUi();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(14), BackColor = Color.FromArgb(22, 25, 32) };
        header.Controls.Add(new Label
        {
            Text = "Cutter Studio Update Publisher",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(14, 17)
        });

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 300,
            Padding = new Padding(14),
            ColumnCount = 3
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        AddRow(form, 0, "GitHub owner", _ownerBox);
        AddRow(form, 1, "Repository", _repoBox);
        AddRow(form, 2, "Release tag", _tagBox);
        AddRow(form, 3, "Release title", _titleBox);
        AddRow(form, 4, "Notes", _notesBox);
        AddRow(form, 5, "ZIP / EXE file", _assetBox);

        var browseButton = new Button { Text = "Browse" };
        browseButton.Click += (_, _) => BrowseAsset();
        form.Controls.Add(browseButton, 2, 5);

        _publishButton.Click += async (_, _) => await PublishAsync();
        form.Controls.Add(_publishButton, 1, 6);

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
        try
        {
            var owner = Required(_ownerBox.Text, "GitHub owner");
            var repo = Required(_repoBox.Text, "Repository");
            var tag = Required(_tagBox.Text, "Release tag");
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
            await UploadAssetAsync(client, release.UploadUrl, assetPath, assetName);
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
        }
    }

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
        foreach (var asset in release.Assets)
        {
            if (!asset.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                continue;
            Log("Deleting existing asset...");
            var response = await client.DeleteAsync(asset.Url);
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task UploadAssetAsync(HttpClient client, string uploadUrlTemplate, string path, string assetName)
    {
        Log("Uploading asset...");
        var uploadUrl = uploadUrlTemplate.Split('{')[0] + "?name=" + Uri.EscapeDataString(assetName);
        await using var stream = File.OpenRead(path);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? "application/zip"
            : "application/octet-stream");
        var response = await client.PostAsync(uploadUrl, content);
        response.EnsureSuccessStatusCode();
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);

    private static string Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
        return value.Trim();
    }

    private void Log(string text) => _logBox.AppendText($"[{DateTime.Now:T}] {text}{Environment.NewLine}");

    private sealed record GitHubRelease(string UploadUrl, IReadOnlyList<GitHubAsset> Assets);
    private sealed record GitHubAsset(string Name, string Url);
}
