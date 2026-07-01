using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace CutterStudio.LicenseAdmin;

public sealed class MainForm : Form
{
    private readonly LicenseRepository _repository;
    private readonly string _apiStatus;
    private readonly BindingList<LicenseGridRow> _rows = [];
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly TextBox _nameBox = new() { PlaceholderText = "Customer name" };
    private readonly TextBox _emailBox = new() { PlaceholderText = "Customer email" };
    private readonly TextBox _notesBox = new() { PlaceholderText = "Notes", Multiline = true, Height = 56 };
    private readonly DateTimePicker _expiryPicker = new() { Format = DateTimePickerFormat.Short };
    private readonly CheckBox _neverExpiresBox = new() { Text = "No expiry", AutoSize = true };
    private readonly NumericUpDown _maxActivationsBox = new() { Minimum = 1, Maximum = 1000, Value = 1 };
    private readonly TextBox _publicUrlBox = new() { PlaceholderText = "Public license URL, e.g. https://your-name.ngrok-free.app" };
    private readonly Label _statusLabel = new() { AutoSize = true };
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 5000 };

    public MainForm(LicenseRepository repository, string apiStatus)
    {
        _repository = repository;
        _apiStatus = apiStatus;

        Text = $"Cutter Studio License Admin v{AppVersion()}";
        Width = 1180;
        Height = 720;
        MinimumSize = new Size(980, 600);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        Load += async (_, _) => await RefreshLicensesAsync();
        _refreshTimer.Tick += async (_, _) => await RefreshLicensesAsync(false);
        _refreshTimer.Start();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(14), BackColor = Color.FromArgb(22, 25, 32) };
        var title = new Label
        {
            Text = $"Cutter Studio License Admin v{AppVersion()}",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(14, 10)
        };
        var server = new Label
        {
            Text = _apiStatus,
            ForeColor = Color.FromArgb(160, 210, 255),
            AutoSize = true,
            Location = new Point(18, 38)
        };
        header.Controls.Add(title);
        header.Controls.Add(server);

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 330 };

        var formPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 18
        };
        formPanel.RowStyles.Clear();
        for (var i = 0; i < 12; i++)
            formPanel.RowStyles.Add(new RowStyle(i == 8 ? SizeType.Percent : SizeType.AutoSize, i == 8 ? 100 : 0));

        var createButton = new Button { Text = "Create License", Height = 36 };
        var refreshButton = new Button { Text = "Refresh", Height = 32 };
        var copyButton = new Button { Text = "Copy Selected Key", Height = 32 };
        var blockButton = new Button { Text = "Block / Unblock Selected", Height = 32 };
        var saveClientConfigButton = new Button { Text = "Save Client Public URL Config", Height = 32 };

        createButton.Click += async (_, _) => await CreateLicenseAsync();
        refreshButton.Click += async (_, _) => await RefreshLicensesAsync();
        copyButton.Click += (_, _) => CopySelectedKey();
        blockButton.Click += async (_, _) => await ToggleSelectedBlockAsync();
        saveClientConfigButton.Click += (_, _) => SaveClientConfig();
        _neverExpiresBox.CheckedChanged += (_, _) => _expiryPicker.Enabled = !_neverExpiresBox.Checked;

        formPanel.Controls.Add(MakeLabel("Customer name"));
        formPanel.Controls.Add(_nameBox);
        formPanel.Controls.Add(MakeLabel("Customer email"));
        formPanel.Controls.Add(_emailBox);
        formPanel.Controls.Add(MakeLabel("Expiry date"));
        formPanel.Controls.Add(_expiryPicker);
        formPanel.Controls.Add(_neverExpiresBox);
        formPanel.Controls.Add(MakeLabel("Max activations"));
        formPanel.Controls.Add(_maxActivationsBox);
        formPanel.Controls.Add(MakeLabel("Notes"));
        formPanel.Controls.Add(_notesBox);
        formPanel.Controls.Add(createButton);
        formPanel.Controls.Add(refreshButton);
        formPanel.Controls.Add(copyButton);
        formPanel.Controls.Add(blockButton);
        formPanel.Controls.Add(MakeLabel("Public URL for customer apps"));
        formPanel.Controls.Add(_publicUrlBox);
        formPanel.Controls.Add(saveClientConfigButton);
        formPanel.Controls.Add(_statusLabel);

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Key", DataPropertyName = nameof(LicenseGridRow.Key), Width = 230 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Customer", DataPropertyName = nameof(LicenseGridRow.Customer), Width = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Email", DataPropertyName = nameof(LicenseGridRow.Email), Width = 170 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Expires", DataPropertyName = nameof(LicenseGridRow.Expires), Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Use", DataPropertyName = nameof(LicenseGridRow.Use), Width = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Open now", DataPropertyName = nameof(LicenseGridRow.OpenNow), Width = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Last seen", DataPropertyName = nameof(LicenseGridRow.LastSeen), Width = 145 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", DataPropertyName = nameof(LicenseGridRow.Status), Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Notes", DataPropertyName = nameof(LicenseGridRow.Notes), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.DataSource = _rows;

        split.Panel1.Controls.Add(formPanel);
        split.Panel2.Controls.Add(_grid);
        Controls.Add(split);
        Controls.Add(header);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 8, 0, 2),
        Font = new Font("Segoe UI", 9, FontStyle.Bold)
    };

    private static string AppVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.1";

    private async Task CreateLicenseAsync()
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show("Enter customer name first.", "License Admin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DateTime? expiry = _neverExpiresBox.Checked
            ? null
            : DateTime.SpecifyKind(_expiryPicker.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime();

        var license = await _repository.CreateLicenseAsync(
            _nameBox.Text,
            _emailBox.Text,
            expiry,
            (int)_maxActivationsBox.Value,
            _notesBox.Text);

        Clipboard.SetText(license.LicenseKey);
        _statusLabel.Text = "License created and copied to clipboard.";
        _nameBox.Clear();
        _emailBox.Clear();
        _notesBox.Clear();
        await RefreshLicensesAsync();
    }

    private async Task RefreshLicensesAsync(bool showStatus = true)
    {
        _rows.Clear();
        var licenses = await _repository.GetLicensesAsync();
        foreach (var license in licenses)
            _rows.Add(new LicenseGridRow(license));
        if (showStatus)
            _statusLabel.Text = $"Loaded {licenses.Count} license(s).";
    }

    private void CopySelectedKey()
    {
        var row = SelectedRow();
        if (row is null)
            return;
        Clipboard.SetText(row.Key);
        _statusLabel.Text = "Selected license key copied.";
    }

    private async Task ToggleSelectedBlockAsync()
    {
        var row = SelectedRow();
        if (row is null)
            return;
        await _repository.SetLicenseBlockedAsync(row.Id, !row.IsBlocked);
        await RefreshLicensesAsync();
    }

    private LicenseGridRow? SelectedRow() =>
        _grid.CurrentRow?.DataBoundItem as LicenseGridRow;

    private void SaveClientConfig()
    {
        var publicUrl = _publicUrlBox.Text.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(publicUrl) ||
            !Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show("Enter a valid public URL first.", "License Admin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var defaultClientDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\CutterStudio"));
        Directory.CreateDirectory(defaultClientDirectory);
        var path = Path.Combine(defaultClientDirectory, "license-server.json");
        var json = JsonSerializer.Serialize(new { licenseServerUrl = publicUrl }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        _statusLabel.Text = $"Client config saved: {path}";
        MessageBox.Show(
            $"Client config saved.\n\nShip this file with CutterStudio.exe:\n{path}",
            "License Admin",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private sealed class LicenseGridRow
    {
        public LicenseGridRow(LicenseRecord license)
        {
            Id = license.Id;
            Key = license.LicenseKey;
            Customer = license.CustomerName;
            Email = license.CustomerEmail;
            Expires = license.ExpiresUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "Never";
            Use = $"{license.ActivationsUsed}/{license.MaxActivations}";
            OpenNow = license.LastSeenUtc is not null && DateTime.UtcNow - license.LastSeenUtc.Value <= TimeSpan.FromSeconds(75)
                ? "Yes"
                : "No";
            LastSeen = license.LastSeenUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            IsBlocked = license.IsBlocked;
            Status = license.IsBlocked
                ? "Blocked"
                : license.ExpiresUtc is not null && license.ExpiresUtc.Value < DateTime.UtcNow
                    ? "Expired"
                    : "Active";
            Notes = license.Notes;
        }

        public long Id { get; }
        public string Key { get; }
        public string Customer { get; }
        public string Email { get; }
        public string Expires { get; }
        public string Use { get; }
        public string OpenNow { get; }
        public string LastSeen { get; }
        public bool IsBlocked { get; }
        public string Status { get; }
        public string Notes { get; }
    }
}
