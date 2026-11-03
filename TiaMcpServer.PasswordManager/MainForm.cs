using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Shared.PasswordManagement;

namespace TiaMcpServer.PasswordManager;

/// <summary>
/// Two tabs: "Passwörter", the entry list of the password map (purpose, project, PLC, source (credential target or
/// environment variable name) and the last check; every change is written to the map file at once), and "Zugriffsmodus"
/// (<see cref="AccessModePanel"/>), the server's access mode in the settings file. The server reads both at its start.
/// </summary>
internal sealed class MainForm : Form
{
    public const string Caption = "TiaMcp Passwortverwaltung";

    private readonly PasswordManagerOptions _options;
    private readonly ICredentialStore _store;
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
    private readonly Dictionary<PasswordMapEntry, CredentialCheckResult> _checks = new();
    private readonly Button _change = new() { Text = "Ändern…", AutoSize = true, Enabled = false };
    private readonly Button _delete = new() { Text = "Löschen", AutoSize = true, Enabled = false };
    private readonly Button _test = new() { Text = "Test", AutoSize = true, Enabled = false };
    private PasswordMapEditor? _editor;

    public MainForm(PasswordManagerOptions options, ICredentialStore store)
    {
        _options = options;
        _store = store;
        Text = Caption;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(980, 520);
        MinimumSize = new Size(720, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(10);

        _list.Columns.Add("Zweck", 260);
        _list.Columns.Add("Projekt", 150);
        _list.Columns.Add("PLC", 110);
        _list.Columns.Add("Quelle", 290);
        _list.Columns.Add("Test", 140);
        _list.SelectedIndexChanged += (_, _) => SelectionChanged();
        _list.DoubleClick += (_, _) => ChangeClicked();

        var add = new Button { Text = "Hinzufügen…", AutoSize = true };
        var testAll = new Button { Text = "Alle testen", AutoSize = true };
        var close = new Button { Text = "Schließen", AutoSize = true };
        add.Click += (_, _) => AddClicked();
        _change.Click += (_, _) => ChangeClicked();
        _delete.Click += (_, _) => DeleteClicked();
        _test.Click += (_, _) => TestClicked(Selected is { } entry ? new[] { entry } : Array.Empty<PasswordMapEntry>());
        testAll.Click += (_, _) => TestClicked(_editor?.Entries.ToArray() ?? Array.Empty<PasswordMapEntry>());
        close.Click += (_, _) => Close();

        var header = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 8),
            Text = $"Zuordnungsdatei: {options.MapPath}\r\n"
                   + $"Präfix der Anmeldeinformationen: {(options.Prefix == PasswordSources.AnyCredentialPrefix ? "* (jedes Ziel; neue Einträge unter " + PasswordSources.CredentialTargetPrefix + ")" : options.Prefix)} ({options.PrefixSource})\r\n"
                   + "Passwörter werden nur hier eingegeben und in der Windows-Anmeldeinformationsverwaltung dieses Benutzers gespeichert; "
                   + "die Datei enthält nur Namen. Änderungen an der Datei gelten nach einem Neustart des MCP-Servers.",
        };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange(new Control[] { add, _change, _delete, _test, testAll, close });
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_list, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        layout.Controls.Add(_status, 0, 3);
        var passwordsTab = new TabPage("Passwörter") { Padding = new Padding(6) };
        passwordsTab.Controls.Add(layout);
        var accessModeTab = new TabPage(ServerSettingsEditor.SectionTitle) { Padding = new Padding(6) };
        accessModeTab.Controls.Add(new AccessModePanel(options.SettingsPath!));
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(passwordsTab);
        tabs.TabPages.Add(accessModeTab);
        Controls.Add(tabs);
        Load += (_, _) => LoadMap();
    }

    private PasswordMapEntry? Selected => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as PasswordMapEntry : null;

    private void LoadMap()
    {
        var path = _options.MapPath!;
        try
        {
            _editor = PasswordMapEditor.FromJson(File.Exists(path) ? File.ReadAllText(path) : null, _options.Prefix);
            _status.Text = File.Exists(path) ? $"{_editor.Entries.Count} Einträge geladen." : "Noch keine Zuordnungsdatei; sie wird beim ersten Speichern angelegt.";
        }
        catch (PasswordMapException exception)
        {
            // Never overwrite a file the server refuses: the owner fixes or removes it first.
            MessageBox.Show(this, exception.Message + "\r\n\r\nDie Datei wird nicht verändert. Bitte korrigieren oder entfernen und das Programm neu starten.\r\n" + path,
                Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Die Zuordnungsdatei kann nicht gelesen werden ({exception.GetType().Name}).\r\n{path}", Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        Refill(null);
    }

    private void Refill(PasswordMapEntry? select)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var entry in _editor!.Entries)
        {
            var item = new ListViewItem(PasswordPurposes.LabelOf(entry.Purpose)) { Tag = entry };
            item.SubItems.Add(PasswordMapEditor.DescribeProject(entry));
            item.SubItems.Add(PasswordMapEditor.DescribePlc(entry));
            item.SubItems.Add(PasswordMapEditor.DescribeSource(entry));
            item.SubItems.Add(_checks.TryGetValue(entry, out var check) ? (check.Usable ? "in Ordnung" : "fehlt / Problem") : "");
            _list.Items.Add(item);
            if (ReferenceEquals(entry, select)) item.Selected = true;
        }

        _list.EndUpdate();
        SelectionChanged();
    }

    private void SelectionChanged()
    {
        var selected = Selected is not null;
        _change.Enabled = _delete.Enabled = _test.Enabled = selected;
    }

    private void AddClicked() => Edit(null);

    private void ChangeClicked()
    {
        if (Selected is { } entry) Edit(entry);
    }

    private void Edit(PasswordMapEntry? replacing)
    {
        if (_editor is null) return;
        using var dialog = new EntryDialog(_editor, _store, replacing);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result?.Entry is not { } entry) return;
        if (!WriteMap()) return;
        _status.Text = "Gespeichert: " + PasswordPurposes.LabelOf(entry.Purpose) + ", " + PasswordMapEditor.DescribeSource(entry) + "."
                       + (dialog.Result.SharedWith > 0 ? $" Dieselbe Anmeldeinformation nutzen {dialog.Result.SharedWith} weitere Einträge; sie erhalten das neue Passwort ebenfalls." : "");
        if (replacing is not null) _checks.Remove(replacing);
        if (dialog.Result.ObsoleteTarget is { } obsolete) OfferDelete(obsolete);
        Refill(entry);
    }

    private void DeleteClicked()
    {
        if (_editor is null || Selected is not { } entry) return;
        if (MessageBox.Show(this, $"Eintrag \"{PasswordPurposes.LabelOf(entry.Purpose)}\" ({PasswordMapEditor.DescribeProject(entry)}, {PasswordMapEditor.DescribePlc(entry)}) aus der Zuordnungsdatei löschen?",
                Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var target = _editor.Remove(entry);
        _checks.Remove(entry);
        if (!WriteMap()) return;
        _status.Text = "Eintrag gelöscht.";
        if (target is not null) OfferDelete(target);
        Refill(null);
    }

    private void OfferDelete(string target)
    {
        if (MessageBox.Show(this, $"Die Anmeldeinformation \"{target}\" wird von keinem Eintrag mehr verwendet. Auch aus der Windows-Anmeldeinformationsverwaltung löschen?",
                Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _status.Text = _store.Delete(target) is { } error ? error : $"Anmeldeinformation \"{target}\" gelöscht.";
    }

    private void TestClicked(IReadOnlyList<PasswordMapEntry> entries)
    {
        if (_editor is null || entries.Count == 0) return;
        var problems = new List<string>();
        foreach (var entry in entries)
        {
            var result = _editor.Test(entry, _store, Environment.GetEnvironmentVariable);
            _checks[entry] = result;
            if (!result.Usable || entries.Count == 1)
                problems.Add($"{PasswordPurposes.LabelOf(entry.Purpose)} ({PasswordMapEditor.DescribeProject(entry)}, {PasswordMapEditor.DescribePlc(entry)}): {result.Message}");
        }

        Refill(Selected);
        _status.Text = $"{entries.Count} geprüft, {entries.Count(e => _checks[e].Usable)} in Ordnung.";
        if (problems.Count > 0)
            MessageBox.Show(this, string.Join("\r\n\r\n", problems), "Test", MessageBoxButtons.OK, entries.All(e => _checks[e].Usable) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private bool WriteMap()
    {
        try
        {
            _editor!.WriteFile(_options.MapPath!);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PasswordMapException)
        {
            MessageBox.Show(this, $"Die Zuordnungsdatei konnte nicht geschrieben werden: {exception.Message}\r\n{_options.MapPath}", Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
