namespace TiaMcpServer.Contracts.Hmi;

/// <summary>
/// Classic WinCC (Comfort/Advanced panels, <c>Siemens.Engineering.Hmi.HmiTarget</c>) objects in hmi_read/hmi_write
/// (issue #28); worker method names. The worker has no compile reference to <c>Siemens.Engineering.WinCC.dll</c>: every
/// member is bound at runtime by exact name and argument types (evidence: <c>Siemens.Engineering.WinCC.xml</c>, V21).
/// </summary>
public static class HmiClassicMethods
{
    public const string ListObjects = "hmi_list_classic_objects";
    public const string ExportObjects = "hmi_export_classic_objects";
    public const string ImportObjects = "hmi_import_classic_objects";
    public const string DeleteObject = "hmi_delete_classic_object";
    public const string CreateFolder = "hmi_create_classic_folder";
    public const string DeleteFolder = "hmi_delete_classic_folder";

    public static IReadOnlyList<string> Reads { get; } = Array.AsReadOnly(new[] { ListObjects });

    public static IReadOnlyList<string> Writes { get; } = Array.AsReadOnly(new[] { ExportObjects, ImportObjects, DeleteObject, CreateFolder, DeleteFolder });

    public static bool IsClassic(string? method) => method is not null && (Reads.Contains(method) || Writes.Contains(method));
}

/// <summary>
/// One kind of classic WinCC object and where it lives on <c>HmiTarget</c>: the documented root property (a system
/// folder, or the target itself when null), the documented composition of the objects, and which documented members
/// exist for it (V21 <c>Siemens.Engineering.WinCC.xml</c>).
/// </summary>
public sealed class HmiClassicKind
{
    public HmiClassicKind(string name, string? rootProperty, string itemsProperty, bool hasUserFolders, bool canDelete, bool canExport,
        string? singletonImport = null, bool needsTable = false, string? keyProperty = null)
    {
        Name = name;
        RootProperty = rootProperty;
        ItemsProperty = itemsProperty;
        HasUserFolders = hasUserFolders;
        CanDelete = canDelete;
        CanExport = canExport;
        SingletonImport = singletonImport;
        NeedsTable = needsTable;
        KeyProperty = keyProperty;
    }

    /// <summary>The public kind name (classicKind).</summary>
    public string Name { get; }

    /// <summary>The <c>HmiTarget</c> property holding the system folder; null when the composition is on the target itself.</summary>
    public string? RootProperty { get; }

    /// <summary>The property of the folder (or target) holding the composition; for a singleton, the target property of the object.</summary>
    public string ItemsProperty { get; }

    /// <summary>The system folder has a <c>Folders</c> composition of user folders (<c>Create(String)</c>, folder <c>Delete()</c>).</summary>
    public bool HasUserFolders { get; }

    public bool CanDelete { get; }

    /// <summary>False only for graphic lists: their export is reported to crash TIA Portal V21 (V21 pitfall 3).</summary>
    public bool CanExport { get; }

    /// <summary>For a singleton (one object, no composition): the <c>HmiTarget</c> import method; null otherwise.</summary>
    public string? SingletonImport { get; }

    public bool IsSingleton => SingletonImport is not null;

    /// <summary>Tags live in a tag table: <c>tableName</c> selects it.</summary>
    public bool NeedsTable { get; }

    /// <summary>The property naming an object when it has no <c>Name</c> (slide-in screens: <c>SlideinType</c>).</summary>
    public string? KeyProperty { get; }
}

public static class HmiClassicKinds
{
    public const string TagTables = "tagTables";
    public const string Tags = "tags";
    public const string Screens = "screens";
    public const string ScreenTemplates = "screenTemplates";
    public const string ScreenPopups = "screenPopups";
    public const string ScreenSlideins = "screenSlideins";
    public const string VbScripts = "vbScripts";
    public const string TextLists = "textLists";
    public const string GraphicLists = "graphicLists";
    public const string Connections = "connections";
    public const string Cycles = "cycles";
    public const string ScreenGlobalElements = "screenGlobalElements";
    public const string ScreenOverview = "screenOverview";

    /// <summary>The message of the refused graphic list export (V21 pitfall 3), shared by host guard text and worker.</summary>
    public const string GraphicListExportSkipped = "skipped: reported to crash TIA Portal V21 (exporting a classic graphic list, V21 pitfall 3); nothing was written.";

    public static IReadOnlyList<HmiClassicKind> All { get; } = Array.AsReadOnly(new[]
    {
        new HmiClassicKind(TagTables, "TagFolder", "TagTables", hasUserFolders: true, canDelete: true, canExport: true),
        new HmiClassicKind(Tags, "TagFolder", "Tags", hasUserFolders: false, canDelete: true, canExport: true, needsTable: true),
        new HmiClassicKind(Screens, "ScreenFolder", "Screens", hasUserFolders: true, canDelete: true, canExport: true),
        new HmiClassicKind(ScreenTemplates, "ScreenTemplateFolder", "ScreenTemplates", hasUserFolders: true, canDelete: true, canExport: true),
        new HmiClassicKind(ScreenPopups, "ScreenPopupFolder", "ScreenPopups", hasUserFolders: true, canDelete: true, canExport: true),
        // ScreenSlidein has no Name and no Delete in V21; it is named by its SlideinType.
        new HmiClassicKind(ScreenSlideins, "ScreenSlideinFolder", "ScreenSlideins", hasUserFolders: false, canDelete: false, canExport: true, keyProperty: "SlideinType"),
        new HmiClassicKind(VbScripts, "VBScriptFolder", "VBScripts", hasUserFolders: true, canDelete: true, canExport: true),
        new HmiClassicKind(TextLists, null, "TextLists", hasUserFolders: false, canDelete: true, canExport: true),
        new HmiClassicKind(GraphicLists, null, "GraphicLists", hasUserFolders: false, canDelete: true, canExport: false),
        new HmiClassicKind(Connections, null, "Connections", hasUserFolders: false, canDelete: true, canExport: true),
        new HmiClassicKind(Cycles, null, "Cycles", hasUserFolders: false, canDelete: true, canExport: true),
        new HmiClassicKind(ScreenGlobalElements, null, "ScreenGlobalElements", hasUserFolders: false, canDelete: false, canExport: true, singletonImport: "ImportScreenGlobalElements"),
        new HmiClassicKind(ScreenOverview, null, "ScreenOverview", hasUserFolders: false, canDelete: false, canExport: true, singletonImport: "ImportScreenOverview"),
    });

    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(All.Select(k => k.Name).ToArray());

    public static HmiClassicKind? Find(string? name) => All.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.Ordinal));

    /// <summary>The kinds whose screens carry a screen <c>Number</c> (V21 pitfall 4: a duplicate is reported to terminate TIA Portal).</summary>
    public static bool IsNumberedScreen(string kind) => kind == Screens;
}

/// <summary>Result of <c>list_classic_objects</c>: the objects of one kind of a classic WinCC HMI, ordered by folder and name.</summary>
public class HmiClassicObjectListInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public string HmiName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string? GroupPath { get; set; }

    public string? TableName { get; set; }

    public List<HmiClassicObjectInfo> Objects { get; set; } = new List<HmiClassicObjectInfo>();

    /// <summary>The user folder paths below the system folder (slash-separated); empty for kinds without folders.</summary>
    public List<string> Folders { get; set; } = new List<string>();
}

public class HmiClassicObjectInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Slash-separated user folder path below the system folder; null at the system folder's top level or for kinds without folders.</summary>
    public string? FolderPath { get; set; }

    /// <summary>The tag table of a tag; null for the other kinds.</summary>
    public string? TableName { get; set; }

    /// <summary><c>IsSystemObject</c> of tag tables and cycles; null where the kind has no such property or it is unreadable.</summary>
    public bool? IsSystemObject { get; set; }

    /// <summary>The runtime type name, e.g. <c>Screen</c>, <c>TagTable</c>.</summary>
    public string TypeName { get; set; } = string.Empty;
}

/// <summary>
/// Classic screen numbers in a SimaticML document (V21 pitfall 4). Siemens-free so the host tests it: every element whose
/// local name is <c>Hmi.Screen.Screen</c> contributes its <c>AttributeList/Name</c> and <c>AttributeList/Number</c>.
/// </summary>
public static class HmiClassicScreenNumbers
{
    public static List<(string Name, int Number)> Read(System.Xml.Linq.XDocument document)
    {
        var result = new List<(string, int)>();
        foreach (var screen in document.Descendants().Where(e => e.Name.LocalName == "Hmi.Screen.Screen"))
        {
            var attributes = screen.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeList");
            var name = attributes?.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value?.Trim();
            var number = attributes?.Elements().FirstOrDefault(e => e.Name.LocalName == "Number")?.Value?.Trim();
            if (name is not null && int.TryParse(number, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                result.Add((name, value));
        }

        return result;
    }

    /// <summary>
    /// The conflicts an import of <paramref name="incoming"/> would create: a number used twice in the file, or used by an
    /// existing screen of another name (a same-named existing screen is the one an overriding import replaces).
    /// </summary>
    public static List<string> Conflicts(IReadOnlyList<(string Name, int Number)> incoming, IReadOnlyList<(string Name, int Number)> existing)
    {
        var conflicts = new List<string>();
        foreach (var group in incoming.GroupBy(s => s.Number).Where(g => g.Count() > 1))
            conflicts.Add($"screen number {group.Key} is used {group.Count()} times in the file ({string.Join(", ", group.Select(s => s.Name))})");
        foreach (var screen in incoming)
            foreach (var other in existing.Where(e => e.Number == screen.Number && !string.Equals(e.Name, screen.Name, StringComparison.OrdinalIgnoreCase)))
                conflicts.Add($"screen '{screen.Name}' has number {screen.Number}, which existing screen '{other.Name}' uses");
        return conflicts.Distinct(StringComparer.Ordinal).ToList();
    }
}
