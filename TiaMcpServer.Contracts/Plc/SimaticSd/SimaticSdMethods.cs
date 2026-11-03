namespace TiaMcpServer.Contracts.Plc.SimaticSd;

/// <summary>
/// The SIMATIC SD (.s7dcl/.s7res) operations of plc_read/plc_write. SIMATIC SD is the V21 block document format of
/// <c>PlcBlock.ExportAsDocuments</c> / <c>PlcBlockComposition.ImportFromDocuments(..., ImportDocumentOptions.Override)</c>,
/// which the Siemens V21 skill <c>simatic-sd</c> (MIT, Siemens AG) names "the correct path for adding LAD/FBD/SCL networks
/// to existing blocks via Openness". Not live-tested.
/// </summary>
public static class SimaticSdMethods
{
    /// <summary>plc_read: the block header pragmas and every network of a block's .s7dcl, with titles and comments from its .s7res. Host computation over get_block_content sd.</summary>
    public const string ListNetworks = "list_networks";

    /// <summary>plc_write: a network inserted into the sd export, imported through update_block_logic sd with the export's hash.</summary>
    public const string InsertNetwork = "insert_network";

    /// <summary>plc_write: one network's body replaced in the sd export (its pragma keys kept), imported like insert_network.</summary>
    public const string ReplaceNetwork = "replace_network";

    /// <summary>plc_write: one network (with its pragma) removed from the sd export, imported like insert_network.</summary>
    public const string DeleteNetwork = "delete_network";

    /// <summary>plc_write worker method: a new block from an sd bundle, ImportFromDocuments into a block group.</summary>
    public const string CreateBlockFromDocument = "create_block_from_document";

    /// <summary>Host-only reads (policy-catalog entries, no worker method).</summary>
    public static IReadOnlyList<string> Reads { get; } = Array.AsReadOnly(new[] { ListNetworks });

    /// <summary>The network edits: planned host-side on the sd export, they reach the worker as update_block_logic sd.</summary>
    public static IReadOnlyList<string> NetworkEdits { get; } = Array.AsReadOnly(new[] { InsertNetwork, ReplaceNetwork, DeleteNetwork });
}
