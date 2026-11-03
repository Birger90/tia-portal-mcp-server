using System.ComponentModel;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;

namespace TiaMcpServer.Plc;

/// <summary>The fields of the operations adopted from BetterTIA (network texts, renames, comment replacement).</summary>
public sealed partial class PlcOperationRequest
{
    [Description("set_network_text, replace_network, delete_network (required): the network number, 1-based in the order TIA Portal numbers the networks of the block (list_networks shows it). In one call, a later item numbers the networks as the earlier items of the call left them.")]
    public int? NetworkNumber { get; set; }

    [Description("set_network_text: the new network title (an empty text clears it); give title, comment (the shared comment field) or both.")]
    public string? Title { get; set; }

    [Description("set_network_text, replace_network_text, replace_tag_comments: the language of the texts, e.g. en-US or de-DE. set_network_text: needed when the text exists in several languages; a language the network has no text for yet is added. replace_network_text, replace_tag_comments: omitted replaces in every language. set_plc_project_language (required): the active project language assigned to the MultilingualSupport row (Language.Culture).")]
    public string? Culture { get; set; }

    [Description("replace_network_text, replace_tag_comments (required): 1 to 100 { find, replace } pairs applied in one pass (literal, case-sensitive, longest find first), so a swap of two texts works.")]
    public List<PlcTextReplacementInfo>? Replacements { get; set; }

    [Description("rename_members (required): 1 to 100 { memberPath, newName, section? } renames of members of the blockPath block or the typePath PLC data type, applied together (swaps and rotations work); section is optional for a block when exactly one section holds the path. A member and one of its own members cannot be renamed in one item.")]
    public List<PlcMemberRenameInfo>? MemberRenames { get; set; }

    [Description("rename_tags, rename_objects (required): 1 to 200 { name, newName } renames by name, applied in one ExclusiveAccess transaction (swaps, rotations and chains work through temporary names; any failure rolls every rename back). rename_tags: PLC tags across every tag table of the PLC; rename_objects: blocks, PLC data types and technology objects of the PLC (a name found in more than one kind is ambiguous).")]
    public List<PlcNameRenameInfo>? Renames { get; set; }

    [Description("replace_network_text: networks (default; every network title and comment) or all (also the block title and comment and the GRAPH sequence, step and transition texts).")]
    public string? TextScope { get; set; }
}
