namespace PulsePoint.Agent.Forms;

public class SettingsForm : Form
{
    private readonly AgentConfig _config;
    private TextBox _urlBox = null!;
    private NumericUpDown _intervalSpinner = null!;
    private ComboBox _nicCombo = null!;
    private Button _saveBtn = null!;
    private Button _cancelBtn = null!;
    private List<(string Name, string Ip)> _nics = [];

    public AgentConfig Result { get; private set; }

    public SettingsForm(AgentConfig config)
    {
        _config = config;
        Result = config;
        Build();
    }

    private void Build()
    {
        Text = "PulsePoint Agent — Settings";
        Size = new Size(440, 320);
        MinimumSize = Size;
        MaximumSize = Size;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 18, 25);
        ForeColor = Color.FromArgb(226, 232, 240);
        Font = new Font("Segoe UI", 9f);

        var pad = 20;
        var labelColor = Color.FromArgb(107, 114, 128);
        var inputBack = Color.FromArgb(12, 12, 16);
        var inputFore = Color.FromArgb(226, 232, 240);
        var borderColor = Color.FromArgb(34, 34, 48);

        int y = pad;

        // Title
        var title = new Label
        {
            Text = "PulsePoint Agent",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.FromArgb(167, 139, 250),
            Location = new Point(pad, y),
            AutoSize = true
        };
        Controls.Add(title);
        y += 36;

        // Server URL
        AddLabel("Server URL", pad, y, labelColor);
        y += 18;
        _urlBox = new TextBox
        {
            Text = _config.ServerUrl,
            Location = new Point(pad, y),
            Width = 390,
            BackColor = inputBack,
            ForeColor = inputFore,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Cascadia Code", 9f)
        };
        Controls.Add(_urlBox);
        y += 32;

        // Report interval
        AddLabel("Report Interval (seconds)", pad, y, labelColor);
        y += 18;
        _intervalSpinner = new NumericUpDown
        {
            Minimum = 5,
            Maximum = 3600,
            Value = Math.Max(5, _config.IntervalSeconds),
            Location = new Point(pad, y),
            Width = 100,
            BackColor = inputBack,
            ForeColor = inputFore,
            Font = new Font("Cascadia Code", 9f)
        };
        Controls.Add(_intervalSpinner);
        y += 36;

        // NIC picker
        AddLabel("Report IP / NIC", pad, y, labelColor);
        y += 18;
        _nics = MetricsCollector.GetAllNics();
        _nicCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(pad, y),
            Width = 390,
            BackColor = inputBack,
            ForeColor = inputFore,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Cascadia Code", 9f)
        };
        _nicCombo.Items.Add("Auto-detect");
        foreach (var (name, ip) in _nics)
            _nicCombo.Items.Add($"{name} — {ip}");

        _nicCombo.SelectedIndex = 0;
        if (_config.PreferredIp != null)
        {
            for (int i = 1; i < _nicCombo.Items.Count; i++)
            {
                if (_nicCombo.Items[i]!.ToString()!.Contains(_config.PreferredIp))
                {
                    _nicCombo.SelectedIndex = i;
                    break;
                }
            }
        }
        Controls.Add(_nicCombo);
        y += 44;

        // Buttons
        _cancelBtn = MakeButton("Cancel", pad, y, false);
        _saveBtn = MakeButton("Save & Apply", pad + 100, y, true);
        Controls.Add(_cancelBtn);
        Controls.Add(_saveBtn);

        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _saveBtn.Click += (_, _) => Save();
    }

    private void AddLabel(string text, int x, int y, Color color)
    {
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = color,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold)
        });
    }

    private Button MakeButton(string text, int x, int y, bool primary)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(90, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(124, 58, 237) : Color.FromArgb(28, 28, 37),
            ForeColor = Color.FromArgb(226, 232, 240),
            FlatAppearance = { BorderColor = primary ? Color.FromArgb(124, 58, 237) : Color.FromArgb(34, 34, 48) }
        };
    }

    private void Save()
    {
        var url = _urlBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            MessageBox.Show("Please enter a valid server URL (e.g. http://192.168.1.10:5000)",
                "Invalid URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string? preferredIp = null;
        if (_nicCombo.SelectedIndex > 0 && _nicCombo.SelectedIndex - 1 < _nics.Count)
            preferredIp = _nics[_nicCombo.SelectedIndex - 1].Ip;

        Result = new AgentConfig
        {
            ServerUrl = url,
            IntervalSeconds = (int)_intervalSpinner.Value,
            PreferredIp = preferredIp
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
