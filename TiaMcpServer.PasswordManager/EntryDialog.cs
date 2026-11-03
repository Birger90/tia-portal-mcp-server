using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Shared.PasswordManagement;

namespace TiaMcpServer.PasswordManager;

/// <summary>
/// Adds or changes one entry: purpose, scope (every project or one project, optionally one PLC) and the password, typed
/// twice into masked fields ("anzeigen" shows it). The stored password is never read back, so a change always takes a new
/// one. The fields are cleared when the dialog closes.
/// </summary>
internal sealed class EntryDialog : Form
{
    private const string OtherPurpose = "Andere Passwortkonfiguration (Kurzname eingeben)";

    private readonly ComboBox _purpose = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _otherPurpose = new() { Dock = DockStyle.Fill, Enabled = false, PlaceholderText = "z. B. BlockBindingPassword" };
    private readonly Label _usage = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Dock = DockStyle.Fill };
    private readonly RadioButton _allProjects = new() { Text = "Für alle Projekte", AutoSize = true, Checked = true };
    private readonly RadioButton _oneProject = new() { Text = "Nur Projekt:", AutoSize = true };
    private readonly TextBox _project = new() { Dock = DockStyle.Fill, Enabled = false, PlaceholderText = "Projektname ohne Endung, z. B. Anlage_01" };
    private readonly RadioButton _allPlcs = new() { Text = "Für alle PLCs", AutoSize = true, Checked = true };
    private readonly RadioButton _onePlc = new() { Text = "Nur PLC:", AutoSize = true };
    private readonly TextBox _plc = new() { Dock = DockStyle.Fill, Enabled = false, PlaceholderText = "Name der PLC, z. B. PLC_1" };
    private readonly TextBox _secret = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _repetition = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly CheckBox _show = new() { Text = "anzeigen", AutoSize = true };

    private readonly PasswordMapEditor _editor;
    private readonly ICredentialStore _store;
    private readonly PasswordMapEntry? _replacing;

    public EntryDialog(PasswordMapEditor editor, ICredentialStore store, PasswordMapEntry? replacing)
    {
        _editor = editor;
        _store = store;
        _replacing = replacing;
        Text = replacing is null ? "Passwort hinzufügen" : "Passwort ändern";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10);

        foreach (var purpose in PasswordPurposes.Known) _purpose.Items.Add(purpose);
        _purpose.Items.Add(OtherPurpose);
        _purpose.SelectedIndexChanged += (_, _) => PurposeChanged();
        _oneProject.CheckedChanged += (_, _) => _project.Enabled = _oneProject.Checked;
        _onePlc.CheckedChanged += (_, _) => _plc.Enabled = _onePlc.Checked;
        _show.CheckedChanged += (_, _) => _secret.UseSystemPasswordChar = _repetition.UseSystemPasswordChar = !_show.Checked;

        var ok = new Button { Text = replacing is null ? "Speichern" : "Neues Passwort speichern", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Abbrechen", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => SaveClicked();
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420));
        void Row(Control? left, Control right)
        {
            layout.Controls.Add(left ?? new Label(), 0, layout.RowCount);
            layout.Controls.Add(right, 1, layout.RowCount);
            layout.RowCount++;
        }

        Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) };
        Row(Caption("Zweck:"), _purpose);
        Row(null, _otherPurpose);
        Row(null, _usage);
        Row(Caption("Projekt:"), _allProjects);
        Row(_oneProject, _project);
        Row(Caption("PLC:"), _allPlcs);
        Row(_onePlc, _plc);
        Row(Caption("Passwort:"), _secret);
        Row(Caption("Wiederholen:"), _repetition);
        Row(null, _show);
        Row(null, new Label
        {
            Text = replacing is null
                ? "Das Passwort wird in der Windows-Anmeldeinformationsverwaltung dieses Benutzers gespeichert; die Zuordnungsdatei erhält nur dessen Namen."
                : "Das gespeicherte Passwort wird nie angezeigt. Zum Ändern das neue Passwort zweimal eingeben.",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            ForeColor = SystemColors.GrayText,
        });
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Row(null, buttons);
        Controls.Add(layout);

        if (replacing is null)
        {
            _purpose.SelectedIndex = 0;
        }
        else
        {
            var known = PasswordPurposes.Find(replacing.Purpose);
            if (known is not null) _purpose.SelectedItem = known;
            else
            {
                _purpose.SelectedItem = OtherPurpose;
                _otherPurpose.Text = replacing.Purpose;
            }

            if (replacing.Project is { } project)
            {
                _oneProject.Checked = true;
                _project.Text = project;
            }

            if (replacing.Plc is { } plc)
            {
                _onePlc.Checked = true;
                _plc.Text = plc;
            }
        }

        FormClosed += (_, _) => ClearSecrets();
    }

    /// <summary>The saved entry's result (set when the dialog closes with OK).</summary>
    public PasswordMapSaveResult? Result { get; private set; }

    private void PurposeChanged()
    {
        var other = Equals(_purpose.SelectedItem, OtherPurpose);
        _otherPurpose.Enabled = other;
        _usage.Text = _purpose.SelectedItem is PasswordPurpose purpose ? "Verwendet von: " + purpose.Usage : "Kurzname der Passwortkonfiguration, nach der TIA Portal fragt.";
    }

    private void SaveClicked()
    {
        var purpose = _purpose.SelectedItem is PasswordPurpose known ? known.Purpose : _otherPurpose.Text.Trim();
        var project = _oneProject.Checked ? _project.Text : null;
        var plc = _onePlc.Checked ? _plc.Text : null;
        var secret = _secret.Text.ToCharArray();
        var repetition = _repetition.Text.ToCharArray();
        try
        {
            var error = PasswordPurposes.Error(purpose)
                        ?? PasswordMapEditor.ScopeError(project, "Projekt")
                        ?? PasswordMapEditor.ScopeError(plc, "PLC")
                        ?? PasswordMapEditor.SecretError(secret, repetition);
            if (error is not null)
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = _editor.Save(project, plc, purpose, secret, _store, _replacing);
            if (!result.Saved)
            {
                MessageBox.Show(this, result.Error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = result;
            DialogResult = DialogResult.OK;
            Close();
        }
        finally
        {
            Array.Clear(secret, 0, secret.Length);
            Array.Clear(repetition, 0, repetition.Length);
        }
    }

    private void ClearSecrets()
    {
        _secret.Clear();
        _repetition.Clear();
    }
}
