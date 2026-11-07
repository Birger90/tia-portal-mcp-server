using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared.PasswordManagement;

namespace TiaMcpServer.PasswordManager;

/// <summary>
/// The tab "Zugriffsmodus": three radio buttons for the server's access mode, the value in the settings file, a save and a
/// reset button and the precedence notice. All logic (texts, reading, validation, atomic write) is in
/// <see cref="ServerSettingsEditor"/>; this control only binds to it.
/// </summary>
internal sealed class AccessModePanel : UserControl
{
    private readonly string _path;
    private readonly Label _current = new() { AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly Button _save = new() { Text = "Speichern", AutoSize = true, Enabled = false };
    private readonly Button _reset = new() { Text = "Einstellung entfernen (Standard read-write)", AutoSize = true, Enabled = false };
    private readonly List<(RadioButton Button, McpAccessMode Mode)> _choices = new();
    private ServerSettingsEditor? _editor;

    public AccessModePanel(string path)
    {
        _path = path;
        Dock = DockStyle.Fill;
        Padding = new Padding(10);

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = ServerSettingsEditor.SectionTitle + " des MCP-Servers",
            Padding = new Padding(0, 0, 0, 4),
        });
        layout.Controls.Add(new Label { AutoSize = true, Text = "Einstellungsdatei: " + path, Padding = new Padding(0, 0, 0, 4) });
        layout.Controls.Add(_current);

        foreach (var choice in ServerSettingsEditor.Choices)
        {
            var radio = new RadioButton { Text = choice.Label, AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            radio.CheckedChanged += (_, _) => _save.Enabled = _editor?.CanSave == true && SelectedMode() is not null;
            _choices.Add((radio, choice.Mode));
            layout.Controls.Add(radio);
            layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(880, 0), Text = choice.Explanation, Padding = new Padding(18, 0, 0, 8) });
        }

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        buttons.Controls.AddRange(new Control[] { _save, _reset });
        layout.Controls.Add(buttons);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(880, 0), Text = ServerSettingsEditor.PrecedenceNotice, Padding = new Padding(0, 8, 0, 0) });
        layout.Controls.Add(_status);
        layout.Controls.Add(new ClaudeBridgePanel(path) { Margin = new Padding(0, 16, 0, 0) });
        Controls.Add(layout);

        _save.Click += (_, _) => SaveClicked(SelectedMode());
        _reset.Click += (_, _) => SaveClicked(null);
        Load += (_, _) => LoadSettings();
    }

    private McpAccessMode? SelectedMode()
    {
        foreach (var (button, mode) in _choices)
            if (button.Checked) return mode;
        return null;
    }

    private void LoadSettings()
    {
        _editor = ServerSettingsEditor.Load(_path);
        foreach (var (button, mode) in _choices)
        {
            button.Checked = _editor.CurrentMode == mode;
            button.Enabled = _editor.CanSave;
        }

        ShowState();
        if (_editor.LoadError is { } error)
        {
            _status.Text = error;
            MessageBox.Show(this, error + "\r\n" + _path, MainForm.Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else
        {
            _status.Text = _editor.FileExists ? "" : "Noch keine Einstellungsdatei; sie wird beim ersten Speichern angelegt.";
        }
    }

    private void ShowState()
    {
        _current.Text = "Aktuell gespeichert: " + _editor!.CurrentDescription;
        _save.Enabled = _editor.CanSave && SelectedMode() is not null;
        _reset.Enabled = _editor.CanSave && _editor.CurrentMode is not null;
    }

    private void SaveClicked(McpAccessMode? mode)
    {
        if (_editor is null) return;
        if (_editor.Save(mode) is { } error)
        {
            MessageBox.Show(this, error + "\r\n" + _path, MainForm.Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (mode is null)
            foreach (var (button, _) in _choices)
                button.Checked = false;
        ShowState();
        _status.Text = _editor.SavedMessage();
    }
}
