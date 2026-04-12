using System.Drawing;

namespace SysDash.EndpointAgent;

public sealed class SettingsForm : Form
{
    private readonly TextBox _serverUrlTextBox;
    private readonly NumericUpDown _intervalNumeric;
    private readonly ComboBox _ipComboBox;

    public AgentConfig? SavedConfig { get; private set; }

    public SettingsForm(AgentConfig initialConfig)
    {
        Text = "SysDash Endpoint Agent Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(430, 240);

        var serverLabel = new Label
        {
            Text = "Server URL and Port",
            AutoSize = true,
            Location = new Point(16, 16),
        };
        _serverUrlTextBox = new TextBox
        {
            Location = new Point(16, 38),
            Width = 390,
            Text = initialConfig.ServerUrl,
        };

        var intervalLabel = new Label
        {
            Text = "Report Interval (seconds)",
            AutoSize = true,
            Location = new Point(16, 76),
        };
        _intervalNumeric = new NumericUpDown
        {
            Location = new Point(16, 98),
            Width = 120,
            Minimum = 5,
            Maximum = 3600,
            Value = Math.Clamp(initialConfig.IntervalSeconds, 5, 3600),
        };

        var ipLabel = new Label
        {
            Text = "Reported NIC/IP",
            AutoSize = true,
            Location = new Point(16, 132),
        };
        _ipComboBox = new ComboBox
        {
            Location = new Point(16, 154),
            Width = 390,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        PopulateIpChoices(initialConfig.PreferredIp);

        var saveButton = new Button
        {
            Text = "Save",
            Width = 90,
            Height = 30,
            Location = new Point(316, 192),
            DialogResult = DialogResult.None,
        };
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = new Button
        {
            Text = "Cancel",
            Width = 90,
            Height = 30,
            Location = new Point(218, 192),
            DialogResult = DialogResult.Cancel,
        };

        Controls.Add(serverLabel);
        Controls.Add(_serverUrlTextBox);
        Controls.Add(intervalLabel);
        Controls.Add(_intervalNumeric);
        Controls.Add(ipLabel);
        Controls.Add(_ipComboBox);
        Controls.Add(saveButton);
        Controls.Add(cancelButton);
    }

    private void PopulateIpChoices(string? preferredIp)
    {
        _ipComboBox.Items.Clear();

        var auto = new IpChoiceItem(null, "Auto (primary active NIC)");
        _ipComboBox.Items.Add(auto);

        foreach (var choice in SystemMetricsProvider.GetAvailableIpv4Choices())
        {
            var item = new IpChoiceItem(choice.IpAddress, $"{choice.InterfaceName} ({choice.IpAddress})");
            _ipComboBox.Items.Add(item);
        }

        var selected = _ipComboBox.Items
            .OfType<IpChoiceItem>()
            .FirstOrDefault(x => string.Equals(x.IpAddress, preferredIp, StringComparison.OrdinalIgnoreCase));

        _ipComboBox.SelectedItem = selected ?? auto;
    }

    private void SaveAndClose()
    {
        var url = _serverUrlTextBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show(this, "Please enter a valid http/https URL including port.", "Invalid URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SavedConfig = new AgentConfig
        {
            ServerUrl = url,
            IntervalSeconds = (int)_intervalNumeric.Value,
            PreferredIp = (_ipComboBox.SelectedItem as IpChoiceItem)?.IpAddress,
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed class IpChoiceItem
    {
        public IpChoiceItem(string? ipAddress, string label)
        {
            IpAddress = ipAddress;
            Label = label;
        }

        public string? IpAddress { get; }
        public string Label { get; }

        public override string ToString()
        {
            return Label;
        }
    }
}
