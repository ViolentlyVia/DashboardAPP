using System.Drawing;

namespace SysDash.EndpointAgent;

public sealed class NicPickerForm : Form
{
    private readonly ComboBox _ipComboBox;

    public string? SelectedPreferredIp { get; private set; }

    public NicPickerForm(string? currentPreferredIp)
    {
        Text = "Choose Reported NIC/IP";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 150);

        var ipLabel = new Label
        {
            Text = "IP to report to server",
            AutoSize = true,
            Location = new Point(16, 16),
        };

        _ipComboBox = new ComboBox
        {
            Location = new Point(16, 38),
            Width = 425,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        PopulateIpChoices(currentPreferredIp);

        var saveButton = new Button
        {
            Text = "Save",
            Width = 90,
            Height = 30,
            Location = new Point(351, 98),
            DialogResult = DialogResult.None,
        };
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = new Button
        {
            Text = "Cancel",
            Width = 90,
            Height = 30,
            Location = new Point(253, 98),
            DialogResult = DialogResult.Cancel,
        };

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
        SelectedPreferredIp = (_ipComboBox.SelectedItem as IpChoiceItem)?.IpAddress;
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
