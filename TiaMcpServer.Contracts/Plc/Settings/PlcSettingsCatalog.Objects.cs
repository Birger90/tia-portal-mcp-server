namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Package G (V21 API completion; not live-tested): operations on any engineering object named by an object path
/// (<see cref="PlcObjectPaths"/>). Evidence: Siemens.Engineering.Base.xml (V21): <c>Multiuser.LocalSession.MarkingService</c>,
/// <c>MarkingService.GetMarkings()</c> (<c>Markings.AllMarkings</c>, <c>.ConflictedMarkings</c>; <c>Marking.MarkedObject</c>, <c>.MarkState</c>),
/// <c>.GetMarkStateInfo(IEngineeringObject)</c> (<c>MarkStateInfo.IsMarkable</c>, <c>.MarkState</c>), <c>.IsMarkable(IEngineeringObject)</c>,
/// <c>.MarkObjects(IEnumerable&lt;IEngineeringObject&gt;)</c>, <c>.UnmarkObjects(...)</c>, enum <c>MarkState</c> (None, IsMarkedByMe, IsMarkedByOthers,
/// IsUptoDate); <c>ObjectIdentifierProvider.GetIdentifier(IEngineeringObject)</c> and <c>.Find(String)</c>;
/// <c>IEngineeringObject.GetCompositionInfos()</c> (<c>EngineeringCompositionInfo.Name</c>, <c>.Type</c>), <c>.GetCreationInfos(String)</c>,
/// <c>.GetComposition(String)</c>, <c>IEngineeringComposition.GetCreationInfos()</c> (<c>EngineeringCreationInfo.Type</c>, <c>.ParameterInfos</c>;
/// <c>EngineeringCreationParameterInfo.Name</c>, <c>.IsMandatory</c>, <c>.SupportedTypes</c>); Siemens.Engineering.Step7.xml (V21):
/// <c>SW.PlcTagProvider.GetLinkedTags()</c> ("Service provider for obtaining Plc Tag from Channel"), <c>Motion.CamDataSupport.LoadCamData(FileInfo,
/// CamDataFormatSeparator)</c>, <c>.LoadCamDataBinary(FileInfo)</c>, <c>.SaveCamData(FileInfo, CamDataFormat, CamDataFormatSeparator)</c>,
/// <c>.SaveCamDataBinary(FileInfo)</c>, <c>.SaveCamDataPointList(FileInfo, CamDataFormatSeparator, Int32)</c>, enums <c>CamDataFormat</c> (MCD,
/// PointList, Scout) and <c>CamDataFormatSeparator</c> (Comma, Tab). How the services are reached is not stated for all of them: they are
/// found among the object's <c>GetServiceInfos()</c> by exact name (ObjectIdentifierProvider: the project's).
/// </summary>
public static class PlcObjectMethods
{
    public const string GetMarkings = "get_markings";
    public const string GetMarkState = "get_mark_state";
    public const string MarkObjects = "mark_objects";
    public const string UnmarkObjects = "unmark_objects";
    public const string GetObjectIdentifier = "get_object_identifier";
    public const string FindObjectByIdentifier = "find_object_by_identifier";
    public const string GetCreationInfos = "get_creation_infos";
    public const string GetLinkedTags = "get_linked_tags";
    public const string SaveCamData = "save_cam_data";
    public const string LoadCamData = "load_cam_data";

    /// <summary>The operations that need a bound Multiuser local session (the MarkingService is a member of LocalSession).</summary>
    public static readonly string[] LocalSessionOnly = { GetMarkings, GetMarkState, MarkObjects, UnmarkObjects };
}

public static class PlcObjectNames
{
    public const string ObjectIdentifierProvider = "Siemens.Engineering.ObjectIdentifierProvider";
    public const string PlcTagProvider = "Siemens.Engineering.SW.PlcTagProvider";
    public const string CamDataSupport = "Siemens.Engineering.SW.TechnologicalObjects.Motion.CamDataSupport";
    public const string CamDataFormat = "Siemens.Engineering.SW.TechnologicalObjects.Motion.CamDataFormat";
    public const string CamDataFormatSeparator = "Siemens.Engineering.SW.TechnologicalObjects.Motion.CamDataFormatSeparator";

    /// <summary>save_cam_data / load_cam_data formats: the documented CamDataFormat members plus Binary (the *Binary methods).</summary>
    public static readonly string[] CamFormats = { "MCD", "Scout", "PointList", "Binary" };

    public static readonly string[] CamSeparators = { "Comma", "Tab" };

    public const int MaxObjects = 100;
    public const int MaxCompositions = 100;
    public const int MaxSamplePoints = 1_000_000;
}

/// <summary>
/// The object path grammar of package G (exact names, case-sensitive like TIA Portal): <c>project</c>; <c>plc:PLC</c> (PLC software);
/// <c>block:BLOCKPATH</c> (as plc_read); <c>type:TYPEPATH</c> (a PLC data type, as plc_read); <c>tagTable:PLC/TABLE</c> (a PLC tag table,
/// unique by name in the PLC); <c>device:DEVICE</c>; <c>deviceItem:DEVICE/ITEM</c> (a device item unique by name below the device);
/// <c>technologyObject:PLC/NAME</c>; <c>channel:DEVICE/ITEM/TYPE/IOTYPE/NUMBER</c> (a channel of a module, TYPE Analog|Digital|..., IOTYPE
/// Input|Output, as <c>Channel.Type</c>/<c>IoType</c>/<c>Number</c> read).
/// </summary>
public static class PlcObjectPaths
{
    public static readonly string[] Kinds = { "project", "plc", "block", "type", "tagTable", "device", "deviceItem", "technologyObject", "channel" };

    /// <summary>(kind, rest) of a well-formed path, or the reason it is not one.</summary>
    public static (string? Kind, string? Remainder, string? Error) Parse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, null, "objectPath is required.");
        if (path == "project") return ("project", string.Empty, null);
        var colon = path!.IndexOf(':');
        if (colon <= 0 || colon == path.Length - 1)
            return (null, null, $"objectPath '{path}' must be project or KIND:NAME with KIND one of {string.Join(", ", Kinds.Skip(1))}.");
        var kind = path.Substring(0, colon);
        var rest = path.Substring(colon + 1);
        if (!Kinds.Contains(kind, StringComparer.Ordinal) || kind == "project")
            return (null, null, $"objectPath kind '{kind}' is unknown (allowed: {string.Join(", ", Kinds)}).");
        var parts = rest.Split('/');
        var need = kind switch { "tagTable" or "deviceItem" or "technologyObject" => 2, "channel" => 5, _ => 1 };
        if (kind is "tagTable" or "deviceItem" or "technologyObject" or "channel" && (parts.Length != need || parts.Any(string.IsNullOrWhiteSpace)))
            return (null, null, $"objectPath '{path}' needs {need} '/'-separated names after '{kind}:'.");
        if (kind == "channel" && !int.TryParse(parts[4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            return (null, null, $"objectPath '{path}': the channel number must be a non-negative integer.");
        return (kind, rest, null);
    }
}

public static partial class PlcSettingsCatalog
{
    /// <summary>Package G has no GitHub issue; its definitions carry this number.</summary>
    public const int PackageG = 0;

    private static void AddObjects()
    {
        Add(new(PlcObjectMethods.GetMarkings, false, PackageG, true, null));
        Add(new(PlcObjectMethods.GetMarkState, false, PackageG, true, null));
        Add(new(PlcObjectMethods.MarkObjects, true, PackageG, true, null));
        Add(new(PlcObjectMethods.UnmarkObjects, true, PackageG, true, null));
        Add(new(PlcObjectMethods.GetObjectIdentifier, false, PackageG, true, PlcObjectNames.ObjectIdentifierProvider));
        Add(new(PlcObjectMethods.FindObjectByIdentifier, false, PackageG, true, PlcObjectNames.ObjectIdentifierProvider));
        Add(new(PlcObjectMethods.GetCreationInfos, false, PackageG, true, null));
        Add(new(PlcObjectMethods.GetLinkedTags, false, PackageG, true, PlcObjectNames.PlcTagProvider));
        Add(new(PlcObjectMethods.SaveCamData, true, PackageG, true, PlcObjectNames.CamDataSupport));
        Add(new(PlcObjectMethods.LoadCamData, true, PackageG, true, PlcObjectNames.CamDataSupport));
    }
}
