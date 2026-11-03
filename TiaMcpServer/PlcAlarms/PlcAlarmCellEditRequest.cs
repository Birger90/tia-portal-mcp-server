using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TiaMcpServer.PlcAlarms;

/// <summary>One cell edit of an update_*_cells operation, addressed exactly as read_*_workbook returned the cell.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcAlarmCellEditRequest
{
    [Description("Worksheet name as read_*_workbook returns it.")]
    public string? Sheet { get; set; }

    [Description("A1 cell reference with upper-case column letters, e.g. C5. An empty cell may be written to add a value or a row.")]
    public string? Cell { get; set; }

    [Description("The cell's current value exactly as read (\"\" for an empty cell); a different current value fails the item before anything is imported.")]
    public string? Expected { get; set; }

    [Description("The new value; \"\" clears a text cell. At most 32767 characters, no control characters except tab, CR and LF.")]
    public string? Value { get; set; }

    [Description("Optional cell kind to write: string or number (an invariant number such as 3 or 2.5). Omitted keeps the current kind (string for an empty cell).")]
    public string? Kind { get; set; }
}
