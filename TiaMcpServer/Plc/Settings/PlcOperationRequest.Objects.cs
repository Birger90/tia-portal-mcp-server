using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>Package G: the object path fields (engineering objects by path) and the cam data fields.</summary>
public sealed partial class PlcOperationRequest
{
    [Description("Package G: the engineering object, as project | plc:PLC | block:BLOCKPATH | type:TYPEPATH | tagTable:PLC/TABLE | device:DEVICE | deviceItem:DEVICE/ITEM | technologyObject:PLC/NAME | channel:DEVICE/ITEM/TYPE/IOTYPE/NUMBER (exact names). get_mark_state, get_object_identifier, get_creation_infos, get_linked_tags (a channel), save_cam_data and load_cam_data (a technology object with CamDataSupport): required.")]
    public string? ObjectPath { get; set; }

    [Description("Package G mark_objects / unmark_objects (required): 1 to 100 object paths (see objectPath), marked or unmarked together in the bound Multiuser local session.")]
    public IReadOnlyList<string>? ObjectPaths { get; set; }

    [Description("Package G save_cam_data (default MCD) / load_cam_data: MCD, Scout, PointList (save only) or Binary (SaveCamDataBinary / LoadCamDataBinary).")]
    public string? CamFormat { get; set; }

    [Description("Package G cam data: the CamDataFormatSeparator, Comma (default) or Tab; not used by Binary.")]
    public string? CamSeparator { get; set; }

    [Description("Package G save_cam_data PointList (required there): the number of sample points, 1 to 1,000,000.")]
    public int? SamplePoints { get; set; }
}
