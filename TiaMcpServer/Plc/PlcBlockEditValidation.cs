using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;

namespace TiaMcpServer.Plc;

/// <summary>Shape rules of the granular block editing operations, checked before any worker call.</summary>
internal static class PlcBlockEditValidation
{
    public static void Validate(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        switch (operation.Operation)
        {
            case PlcBlockEditMethods.AddInterfaceMember:
            case PlcBlockEditMethods.UpdateInterfaceMember:
            case PlcBlockEditMethods.DeleteInterfaceMember:
                InterfaceEdit(operation, prefix, errors);
                break;
            case "create_tag":
            case "update_tag":
                // K22: the tag comment is one culture's text, as for the interface members.
                CommentPair(operation, prefix, errors);
                break;
            case PlcBlockEditMethods.UpdateDbMember:
                if (operation.MemberPath is not null) MemberPath(operation.MemberPath, allowIndices: true, prefix, errors);
                if (operation.StartValue is null && operation.ExternalAccessible is null && operation.ExternalVisible is null
                    && operation.ExternalWritable is null && operation.Retain is null && operation.SetPoint is null)
                    errors.Add($"{prefix}: update_db_member needs startValue or at least one of externalAccessible, externalVisible, externalWritable, retain, setPoint.");
                break;
            case PlcBlockEditMethods.CreateInstanceDb:
                Number(operation, prefix, errors);
                break;
            case PlcBlockEditMethods.RenameBlock:
                BlockName(operation.NewName, prefix, errors);
                break;
            case PlcBlockEditMethods.RenumberBlock:
                if (operation.AutoNumber == false)
                    errors.Add($"{prefix}: autoNumber:false is not a request; give number to set a fixed number.");
                if (operation.Number is null == (operation.AutoNumber != true))
                    errors.Add($"{prefix}: renumber_block takes exactly one of number or autoNumber:true.");
                Number(operation, prefix, errors);
                break;
            case PlcBlockEditMethods.CopyBlock:
                BlockName(operation.NewName, prefix, errors);
                Number(operation, prefix, errors);
                break;
        }
    }

    public static void MemberPath(string memberPath, bool allowIndices, string prefix, List<string> errors)
    {
        try
        {
            _ = PlcMemberPath.Parse(memberPath, allowIndices);
        }
        catch (ArgumentException exception)
        {
            errors.Add($"{prefix}: {exception.Message}");
        }
    }

    private static void CommentPair(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        if (operation.Comment is not null && string.IsNullOrWhiteSpace(operation.CommentCulture))
            errors.Add($"{prefix}: commentCulture (e.g. en-US) is required with comment.");
        if (operation.Comment is null && operation.CommentCulture is not null)
            errors.Add($"{prefix}: commentCulture is only valid with comment.");
    }

    private static void InterfaceEdit(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        var onBlock = !string.IsNullOrWhiteSpace(operation.BlockPath);
        var onType = !string.IsNullOrWhiteSpace(operation.TypePath);
        if (onBlock == onType)
            errors.Add($"{prefix}: give exactly one of blockPath (a block) or typePath (a PLC data type).");
        if (onBlock && string.IsNullOrWhiteSpace(operation.Section))
            errors.Add($"{prefix}: section is required for a block (Input, Output, InOut, Static, Temp, Constant or Return).");
        if (onType && operation.Section is not null && !string.Equals(operation.Section, "None", StringComparison.OrdinalIgnoreCase))
            errors.Add($"{prefix}: a PLC data type has only the section None; omit section.");
        if (operation.MemberPath is not null) MemberPath(operation.MemberPath, allowIndices: false, prefix, errors);
        CommentPair(operation, prefix, errors);
        if (operation.Operation == PlcBlockEditMethods.UpdateInterfaceMember)
        {
            if (operation.NewName is null && operation.DataType is null && operation.StartValue is null
                && operation.ClearStartValue != true && operation.Comment is null)
                errors.Add($"{prefix}: update_interface_member needs at least one of newName, dataType, startValue, clearStartValue:true or comment.");
            if (operation.StartValue is not null && operation.ClearStartValue == true)
                errors.Add($"{prefix}: startValue and clearStartValue cannot be combined.");
        }
    }

    private static void BlockName(string? name, string prefix, List<string> errors)
    {
        if (name is not null && (name.Trim().Length != name.Length || name.IndexOfAny(new[] { '/', '\\', '"' }) >= 0 || name.Any(char.IsControl)))
            errors.Add($"{prefix}: newName '{name}' must not have surrounding blanks, '/', '\\', '\"' or control characters.");
    }

    private static void Number(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        if (operation.Number is < 1 or > 65535)
            errors.Add($"{prefix}: number must be between 1 and 65535; TIA Portal enforces the CPU's own range.");
    }
}
