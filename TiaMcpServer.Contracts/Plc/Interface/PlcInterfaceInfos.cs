namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>One interface section of a SimaticML block or type document (Input, Output, InOut, Static, Temp, Constant, Return or None).</summary>
public class PlcInterfaceSectionInfo
{
    public string Name { get; set; } = string.Empty;

    public List<PlcInterfaceMemberInfo> Members { get; set; } = new List<PlcInterfaceMemberInfo>();
}

/// <summary>
/// One <c>Member</c> element as exported. Only <c>Name</c>, <c>Datatype</c>, <c>Informative</c>,
/// <c>Comment/MultiLanguageText</c>, <c>StartValue</c> and nested <c>Member</c> elements are modeled;
/// every other XML attribute is reported verbatim in <see cref="Attributes"/> and every other child
/// element by name in <see cref="OtherElements"/>.
/// </summary>
public class PlcInterfaceMemberInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Dotted path from the section, e.g. Settings.Limits.Max.</summary>
    public string Path { get; set; } = string.Empty;

    public string? DataType { get; set; }

    /// <summary>Text of the member's own StartValue element; null when the document has none.</summary>
    public string? StartValue { get; set; }

    /// <summary>Informative="true": a system-defined member, read only in this model.</summary>
    public bool Informative { get; set; }

    public List<PlcInterfaceCommentInfo> Comments { get; set; } = new List<PlcInterfaceCommentInfo>();

    public List<PlcXmlAttributeInfo> Attributes { get; set; } = new List<PlcXmlAttributeInfo>();

    public List<string> OtherElements { get; set; } = new List<string>();

    public List<PlcInterfaceMemberInfo> Members { get; set; } = new List<PlcInterfaceMemberInfo>();
}

public class PlcInterfaceCommentInfo
{
    public string Culture { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public class PlcXmlAttributeInfo
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
