using TiaMcpServer.Contracts.Shared.ClaudeBridge;
using TiaMcpServer.Contracts.Shared.PasswordManagement;

namespace TiaMcpServer.PasswordManager;

/// <summary>
/// The "Claude-Brücke" section of the tab "Zugriffsmodus": on/off (default off), command, argument template, time limit,
/// allowed tools and the autostart entry. The settings go into the server settings file (member claudeBridge, version 2)
/// through <see cref="ServerSettingsEditor.SaveBridge"/>; the autostart entry is a .cmd in the Startup folder
/// (<see cref="ClaudeBridgeAutostart"/>). Not live-tested.
/// </summary>
internal sealed class ClaudeBridgePanel : GroupBox
{
    private readonly string _path;
    private readonly CheckBox _enabled = new() { Text = ClaudeBridgeEditorText.EnabledLabel, AutoSize = true };
    private readonly TextBox _command = new() { Width = 600 };
    private readonly TextBox _arguments = new() { Multiline = true, Width = 600, Height = 120, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    private readonly NumericUpDown _timeout = new() { Minimum = ClaudeBridgeSettings.MinTimeoutMinutes, Maximum = ClaudeBridgeSettings.MaxTimeoutMinutes, Width = 80 };
    private readonly TextBox _tools = new() { Multiline = true, Width = 600, Height = 120, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    private readonly CheckBox _autostart = new() { Text = ClaudeBridgeEditorText.AutostartLabel, AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(860, 0) };
    private readonly string _bridgeExecutable = ClaudeBridgeAutostart.DefaultExecutable(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    private readonly string _autostartFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ClaudeBridgeAutostart.FileName);

    public ClaudeBridgePanel(string path)
    {
        _path = path;
        Text = ClaudeBridgeEditorText.Title;
        AutoSize = true;
        Padding = new Padding(8);
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(860, 0), Text = ClaudeBridgeEditorText.Notice, Padding = new Padding(0, 0, 0, 6) });
        layout.Controls.Add(_enabled);
        layout.Controls.Add(new Label { AutoSize = true, Text = ClaudeBridgeEditorText.CommandLabel });
        layout.Controls.Add(_command);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(860, 0), Text = ClaudeBridgeEditorText.ArgumentsLabel });
        layout.Controls.Add(_arguments);
        layout.Controls.Add(new Label { AutoSize = true, Text = ClaudeBridgeEditorText.TimeoutLabel });
        layout.Controls.Add(_timeout);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(860, 0), Text = ClaudeBridgeEditorText.AllowedToolsLabel });
        layout.Controls.Add(_tools);
        layout.Controls.Add(_autostart);
        var save = new Button { Text = "Brücke speichern", AutoSize = true };
        var defaults = new Button { Text = "Standardwerte einsetzen", AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        buttons.Controls.AddRange(new Control[] { save, defaults });
        layout.Controls.Add(buttons);
        layout.Controls.Add(_status);
        Controls.Add(layout);
        save.Click += (_, _) => Save();
        defaults.Click += (_, _) => Show(ClaudeBridgeSettings.Default, keepEnabled: true);
        HandleCreated += (_, _) => Reload();
    }

    private void Reload()
    {
        var editor = ServerSettingsEditor.Load(_path);
        Show(editor.CurrentBridge, keepEnabled: false);
        _autostart.Checked = File.Exists(_autostartFile);
        Enabled = editor.CanSave;
        _status.Text = editor.CanSave ? "" : editor.LoadError ?? "";
    }

    private void Show(ClaudeBridgeSettings settings, bool keepEnabled)
    {
        if (!keepEnabled) _enabled.Checked = settings.Enabled;
        _command.Text = settings.Command;
        _arguments.Text = ClaudeBridgeEditorText.ArgumentsToText(settings.Arguments);
        _timeout.Value = Math.Clamp(settings.TimeoutMinutes, ClaudeBridgeSettings.MinTimeoutMinutes, ClaudeBridgeSettings.MaxTimeoutMinutes);
        _tools.Text = ClaudeBridgeEditorText.ToolsToText(settings.AllowedTools);
    }

    private void Save()
    {
        var settings = new ClaudeBridgeSettings(_enabled.Checked, _command.Text.Trim(), ClaudeBridgeEditorText.ArgumentsFromText(_arguments.Text),
            (int)_timeout.Value, ClaudeBridgeEditorText.ToolsFromText(_tools.Text));
        var editor = ServerSettingsEditor.Load(_path);
        if (editor.SaveBridge(settings) is { } error)
        {
            MessageBox.Show(this, error, MainForm.Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var autostart = "";
        try
        {
            if (_autostart.Checked)
            {
                if (ClaudeBridgeAutostart.Problem(_bridgeExecutable, File.Exists) is { } missing)
                    autostart = " Autostart nicht eingerichtet: " + missing;
                else
                {
                    File.WriteAllText(_autostartFile, ClaudeBridgeAutostart.Content(_bridgeExecutable), System.Text.Encoding.ASCII);
                    autostart = " Autostart: " + _autostartFile;
                }
            }
            else if (File.Exists(_autostartFile))
            {
                File.Delete(_autostartFile);
                autostart = " Autostart entfernt.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            autostart = " Autostart konnte nicht geändert werden: " + exception.Message;
        }

        _status.Text = (settings.Enabled ? "Gespeichert: Brücke an." : "Gespeichert: Brücke aus.") + " Gilt ab der nächsten Anfrage." + autostart;
    }
}
