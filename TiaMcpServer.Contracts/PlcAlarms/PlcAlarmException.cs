namespace TiaMcpServer.Contracts.PlcAlarms;

/// <summary>
/// A refusal or failure of the Siemens-free alarm helpers (workbook cells, caller files); <see cref="Category"/>
/// is a worker failure category. The worker turns it into a categorized failure; nothing escapes uncategorized.
/// </summary>
public sealed class PlcAlarmException : Exception
{
    public PlcAlarmException(string category, string message) : base(message) => Category = category;

    public string Category { get; }
}
