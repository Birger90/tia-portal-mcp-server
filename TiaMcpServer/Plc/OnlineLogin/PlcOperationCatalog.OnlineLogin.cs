namespace TiaMcpServer.Plc;

/// <summary>Audit N2: presence and shape of the compares' onlineUser.</summary>
public static partial class PlcOperationCatalog
{
    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> OnlineLoginOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("onlineUser", op => op.OnlineUser is not null),
    };

    private static void ValidateOnlineLoginItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (op.OnlineUser?.Validate() is { } error) errors.Add($"{prefix}: {error}");
    }
}
