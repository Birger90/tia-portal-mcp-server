using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>
/// Namespace-aware model of one exported SimaticML block or PLC data type document
/// (<c>Document/SW.Blocks.*|SW.Types.*/AttributeList/Interface/Sections</c>). It reads the interface
/// tree and applies interface member edits in place, preserving every node it does not touch.
///
/// <para>
/// Element and attribute names are only those evidenced in the V21 exports the repository's tests
/// and generator carry and the Siemens references list: <c>Sections</c> (namespace
/// <see cref="InterfaceNamespaceV5"/>), <c>Section Name</c>, <c>Member Name/Datatype/Informative</c>,
/// <c>Comment/MultiLanguageText Lang</c>, <c>StartValue</c>, and the attribute-list elements
/// <c>Name</c>, <c>Number</c> and <c>ProgrammingLanguage</c>. Anything else is read through verbatim
/// and never created. Array element start values (<c>Subelement</c>) and member attributes such as
/// retentivity are not modeled because no evidenced document shows them.
/// </para>
///
/// <para>
/// Serialization keeps the original text before the root element (XML declaration) and after it
/// byte for byte, keeps all whitespace nodes, and restores the document's line-break style. Character
/// escaping inside the root may differ from Portal's writer (for example <c>&gt;</c>); the XML is
/// equivalent.
/// </para>
/// </summary>
public sealed class SimaticMlInterfaceDocument
{
    public const string InterfaceNamespaceV5 = "http://www.siemens.com/automation/Openness/SW/Interface/v5";

    /// <summary>Document elements whose interface edits are supported (all evidenced in V21 exports or the generator).</summary>
    public static IReadOnlyList<string> EditableDocumentElements { get; } = Array.AsReadOnly(new[]
    {
        "SW.Blocks.FB", "SW.Blocks.FC", "SW.Blocks.OB", "SW.Blocks.GlobalDB", "SW.Types.PlcStruct",
    });

    private static readonly IReadOnlyCollection<string> ModeledMemberChildren = new[] { "Comment", "StartValue", "Member" };

    private readonly string _prefix;
    private readonly string _suffix;
    private readonly string _newLine;
    private readonly XDocument _document;
    private readonly XElement _object;
    private readonly XElement? _attributeList;
    private readonly XElement? _sections;

    private SimaticMlInterfaceDocument(string prefix, string suffix, string newLine, XDocument document)
    {
        _prefix = prefix;
        _suffix = suffix;
        _newLine = newLine;
        _document = document;
        var root = document.Root!;
        _object = root.Elements().FirstOrDefault(e => e.Name.NamespaceName.Length == 0
                && (e.Name.LocalName.StartsWith("SW.Blocks.", StringComparison.Ordinal) || e.Name.LocalName.StartsWith("SW.Types.", StringComparison.Ordinal)))
            ?? throw PlcInterfaceEditException.Unsupported("The document contains no SimaticML block or PLC data type element.");
        _attributeList = _object.Element("AttributeList");
        _sections = _attributeList?.Element("Interface")?.Elements().FirstOrDefault(e => e.Name.LocalName == "Sections");
    }

    /// <summary>The object element name, e.g. SW.Blocks.FB.</summary>
    /// <summary>The parsed document, for edits outside the interface (network texts, local references); Serialize writes it back.</summary>
    public XDocument Document => _document;

    public string DocumentElement => _object.Name.LocalName;

    public string? ObjectName => _attributeList?.Element("Name")?.Value;

    public string? ProgrammingLanguage => _attributeList?.Element("ProgrammingLanguage")?.Value;

    /// <summary>A fail-safe block: its ProgrammingLanguage carries the F_ prefix (V21 import/export reference §6).</summary>
    public bool IsSafety => ProgrammingLanguage?.StartsWith("F_", StringComparison.Ordinal) == true;

    /// <summary>
    /// Set by the plc_write planner once it has fired the fail-safe guards for the block (acceptance always; the login
    /// block guard when a safety password is set and the session is not logged on); the worker checks the login again
    /// before the import. Without it (a caller outside plc_write planning) a fail-safe document is refused.
    /// </summary>
    public bool AllowSafety { get; set; }

    public bool HasInterface => _sections is not null;

    /// <exception cref="PlcInterfaceEditException">The text is not a SimaticML block or type document.</exception>
    public static SimaticMlInterfaceDocument Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) throw PlcInterfaceEditException.Invalid("The SimaticML document is empty.");
        var rootStart = FindRootStart(xml);
        var rootEnd = xml.LastIndexOf('>');
        if (rootStart < 0 || rootEnd < rootStart) throw PlcInterfaceEditException.Invalid("The SimaticML document has no root element.");
        var suffix = xml.Substring(rootEnd + 1);
        if (!string.IsNullOrWhiteSpace(suffix)) throw PlcInterfaceEditException.Invalid("The SimaticML document has content after its root element.");

        XDocument document;
        try
        {
            var body = xml[0] == '﻿' ? xml.Substring(1) : xml;
            document = XDocument.Parse(body, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            throw PlcInterfaceEditException.Invalid("The SimaticML document is not well-formed XML: " + exception.Message);
        }

        if (document.Root is null || document.Root.Name.LocalName != "Document")
            throw PlcInterfaceEditException.Unsupported("The SimaticML document root is not 'Document'.");
        return new SimaticMlInterfaceDocument(xml.Substring(0, rootStart), suffix,
            xml.Contains("\r\n") ? "\r\n" : "\n", document);
    }

    public List<PlcInterfaceSectionInfo> ReadSections()
        => _sections is null
            ? new List<PlcInterfaceSectionInfo>()
            : _sections.Elements(_sections.Name.Namespace + "Section").Select(section => new PlcInterfaceSectionInfo
            {
                Name = (string?)section.Attribute("Name") ?? string.Empty,
                Members = ReadMembers(section, null),
            }).ToList();

    /// <summary>Applies one edit to this document; the document is unchanged when it throws.</summary>
    /// <exception cref="PlcInterfaceEditException">The edit is invalid for this document.</exception>
    public PlcInterfaceEditOutcome Apply(PlcInterfaceEditRequest edit)
    {
        RequireEditable();
        return edit.Kind switch
        {
            PlcInterfaceEditKind.Add => Add(edit),
            PlcInterfaceEditKind.Update => Update(edit),
            PlcInterfaceEditKind.Delete => Delete(edit),
            _ => throw PlcInterfaceEditException.Invalid($"Unknown interface edit '{edit.Kind}'."),
        };
    }

    /// <summary>copy_block: sets AttributeList/Name and removes AttributeList/Number so the import assigns a number.</summary>
    public void PrepareCopy(string newName)
    {
        var name = _attributeList?.Element("Name") ?? throw PlcInterfaceEditException.Unsupported("The SimaticML document has no AttributeList/Name element.");
        name.Value = newName;
        var number = _attributeList.Element("Number");
        if (number is not null) Remove(number);
    }

    public string Serialize()
    {
        var builder = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.Replace,
            NewLineChars = _newLine,
            ConformanceLevel = ConformanceLevel.Fragment,
        };
        using (var writer = XmlWriter.Create(builder, settings))
        {
            _document.Root!.WriteTo(writer);
        }

        return _prefix + builder + _suffix;
    }

    private PlcInterfaceEditOutcome Add(PlcInterfaceEditRequest edit)
    {
        var section = RequireSection(edit.Section);
        var sectionName = (string)section.Attribute("Name")!;
        RefuseSystemSection(sectionName, "add a member to");
        var name = edit.Path.Last.Name;
        RequireNewName(name);
        var dataType = RequireDataType(edit.DataType);
        var parentPath = edit.Path.Parent;
        var parent = parentPath is null ? section : ResolveMember(section, sectionName, parentPath);
        if (parent != section)
        {
            RefuseInformative(parent, sectionName, parentPath!);
            if (!IsStruct((string?)parent.Attribute("Datatype")))
                throw PlcInterfaceEditException.Unsupported(
                    $"'{sectionName}.{parentPath}' has data type '{(string?)parent.Attribute("Datatype")}'; members can only be added inside a Struct member. PLC data type and FB members belong to that type or FB.");
        }

        if (FindChild(parent, name, exactOnly: false) is not null)
            throw PlcInterfaceEditException.Invalid($"'{sectionName}.{edit.Path}' already exists.");
        if (edit.StartValue is not null) RequireScalarForStartValue(dataType, sectionName, edit.Path);
        if (edit.Comment is not null) RequireCulture(edit.CommentCulture);

        var ns = _sections!.Name.Namespace;
        var member = new XElement(ns + "Member", new XAttribute("Name", name), new XAttribute("Datatype", dataType));
        if (!string.IsNullOrEmpty(edit.Comment))
            member.Add(new XElement(ns + "Comment", new XElement(ns + "MultiLanguageText", new XAttribute("Lang", edit.CommentCulture!), edit.Comment)));
        if (edit.StartValue is not null) member.Add(new XElement(ns + "StartValue", edit.StartValue));

        XElement? before = null;
        if (edit.InsertBefore is not null)
            before = FindChild(parent, edit.InsertBefore, exactOnly: false)
                ?? throw PlcInterfaceEditException.NotFound($"insertBefore member '{edit.InsertBefore}' was not found next to '{sectionName}.{edit.Path}'.");
        InsertMember(parent, member, before);

        var changes = new List<PlcInterfaceFieldChange> { new("member", null, $"{sectionName}.{edit.Path}"), new("dataType", null, dataType) };
        if (edit.StartValue is not null) changes.Add(new("startValue", null, edit.StartValue));
        if (!string.IsNullOrEmpty(edit.Comment)) changes.Add(new($"comment[{edit.CommentCulture}]", null, edit.Comment));
        return new(changes, new PlcInterfaceExpectation
        {
            Section = sectionName, Path = edit.Path.ToString(), Exists = true, DataType = dataType,
            CheckStartValue = edit.StartValue is not null, StartValue = edit.StartValue,
            CommentCulture = string.IsNullOrEmpty(edit.Comment) ? null : edit.CommentCulture, Comment = edit.Comment,
        });
    }

    private PlcInterfaceEditOutcome Update(PlcInterfaceEditRequest edit)
    {
        var section = RequireSection(edit.Section);
        var sectionName = (string)section.Attribute("Name")!;
        var member = ResolveMember(section, sectionName, edit.Path);
        RefuseInformative(member, sectionName, edit.Path);
        var isReturn = string.Equals(sectionName, "Return", StringComparison.Ordinal);
        if (isReturn && (edit.NewName is not null || edit.StartValue is not null || edit.ClearStartValue))
            throw PlcInterfaceEditException.Unsupported("Only the data type and comment of the Return member can be changed.");
        if (edit.NewName is null && edit.DataType is null && edit.StartValue is null && !edit.ClearStartValue && edit.Comment is null)
            throw PlcInterfaceEditException.Invalid("update_interface_member needs at least one of newName, dataType, startValue, clearStartValue or comment.");
        if (edit.StartValue is not null && edit.ClearStartValue)
            throw PlcInterfaceEditException.Invalid("startValue and clearStartValue cannot be combined.");

        var currentName = (string)member.Attribute("Name")!;
        var currentType = (string?)member.Attribute("Datatype");
        var resultingType = edit.DataType is null ? currentType : RequireDataType(edit.DataType);
        var startValue = member.Element(member.Name.Namespace + "StartValue");
        var hasChildren = member.Elements(member.Name.Namespace + "Member").Any();
        if (edit.NewName is not null)
        {
            RequireNewName(edit.NewName);
            var clash = FindChild(member.Parent!, edit.NewName, exactOnly: false);
            if (clash is not null && clash != member)
                throw PlcInterfaceEditException.Invalid($"'{sectionName}.{edit.Path.WithLastName(edit.NewName)}' already exists.");
        }

        if (edit.DataType is not null && hasChildren && !IsStruct(resultingType))
            throw PlcInterfaceEditException.Unsupported($"'{sectionName}.{edit.Path}' contains members; delete them before changing its data type to '{resultingType}'.");
        if (edit.DataType is not null && !string.Equals(currentType?.Trim(), resultingType?.Trim(), StringComparison.OrdinalIgnoreCase))
            RefuseStaleTypeContent(member, startValue, edit, sectionName, currentType, resultingType);
        if (edit.StartValue is not null)
        {
            RequireScalarForStartValue(resultingType, sectionName, edit.Path);
            if (hasChildren) throw PlcInterfaceEditException.Unsupported($"'{sectionName}.{edit.Path}' contains members and has no start value of its own.");
            if (startValue is null) RefuseUnmodeledChildren(member, sectionName, edit.Path, "add a start value to");
        }

        XElement? comment = null;
        XElement? text = null;
        if (edit.Comment is not null)
        {
            RequireCulture(edit.CommentCulture);
            comment = member.Element(member.Name.Namespace + "Comment");
            text = comment?.Elements(member.Name.Namespace + "MultiLanguageText")
                .FirstOrDefault(t => string.Equals((string?)t.Attribute("Lang"), edit.CommentCulture, StringComparison.OrdinalIgnoreCase));
            if (comment is null && edit.Comment.Length > 0) RefuseUnmodeledChildren(member, sectionName, edit.Path, "add a comment to");
        }

        // Every check passed: mutate.
        var changes = new List<PlcInterfaceFieldChange>();
        if (edit.NewName is not null)
        {
            changes.Add(new("name", currentName, edit.NewName));
            member.SetAttributeValue("Name", edit.NewName);
        }

        if (edit.DataType is not null)
        {
            changes.Add(new("dataType", currentType, resultingType));
            member.SetAttributeValue("Datatype", resultingType);
        }

        if (edit.StartValue is not null)
        {
            changes.Add(new("startValue", startValue?.Value, edit.StartValue));
            if (startValue is not null) startValue.Value = edit.StartValue;
            else AppendChild(member, new XElement(member.Name.Namespace + "StartValue", edit.StartValue));
        }
        else if (edit.ClearStartValue)
        {
            changes.Add(new("startValue", startValue?.Value, null));
            if (startValue is not null) Remove(startValue);
        }

        if (edit.Comment is not null)
        {
            var culture = edit.CommentCulture!;
            changes.Add(new($"comment[{culture}]", text?.Value, edit.Comment.Length == 0 ? null : edit.Comment));
            if (edit.Comment.Length == 0)
            {
                if (text is not null) Remove(text);
                if (comment is not null && !comment.Elements().Any()) Remove(comment);
            }
            else if (text is not null)
            {
                text.Value = edit.Comment;
            }
            else
            {
                var entry = new XElement(member.Name.Namespace + "MultiLanguageText", new XAttribute("Lang", culture), edit.Comment);
                if (comment is not null) AppendChild(comment, entry);
                else PrependChild(member, new XElement(member.Name.Namespace + "Comment", entry));
            }
        }

        var resultPath = edit.NewName is null ? edit.Path : edit.Path.WithLastName(edit.NewName);
        return new(changes, new PlcInterfaceExpectation
        {
            Section = sectionName, Path = resultPath.ToString(), Exists = true,
            FormerPath = edit.NewName is not null && !string.Equals(edit.NewName, currentName, StringComparison.OrdinalIgnoreCase) ? edit.Path.ToString() : null,
            NewName = edit.NewName,
            DataType = edit.DataType is null ? null : resultingType,
            CheckStartValue = edit.StartValue is not null || edit.ClearStartValue, StartValue = edit.StartValue,
            CommentCulture = edit.Comment is null ? null : edit.CommentCulture,
            Comment = edit.Comment is { Length: > 0 } ? edit.Comment : null,
        });
    }

    private PlcInterfaceEditOutcome Delete(PlcInterfaceEditRequest edit)
    {
        var section = RequireSection(edit.Section);
        var sectionName = (string)section.Attribute("Name")!;
        if (string.Equals(sectionName, "Return", StringComparison.Ordinal))
            throw PlcInterfaceEditException.Unsupported("The Return member cannot be deleted; change its data type to Void instead.");
        RefuseSystemSection(sectionName, "delete a member from");
        var member = ResolveMember(section, sectionName, edit.Path);
        RefuseInformative(member, sectionName, edit.Path);
        Remove(member);
        return new(new[] { new PlcInterfaceFieldChange("member", $"{sectionName}.{edit.Path}", null) },
            new PlcInterfaceExpectation { Section = sectionName, Path = edit.Path.ToString(), Exists = false });
    }

    private void RequireEditable()
    {
        if (!EditableDocumentElements.Contains(DocumentElement, StringComparer.Ordinal))
            throw PlcInterfaceEditException.Unsupported(
                $"Interface edits support {string.Join(", ", EditableDocumentElements)} documents; this document is '{DocumentElement}'. An instance DB takes its interface from its FB.");
        if (IsSafety && !AllowSafety)
            throw PlcInterfaceEditException.Unsupported($"'{ObjectName}' is a fail-safe block (ProgrammingLanguage '{ProgrammingLanguage}'); its interface is not edited through SimaticML here.");
        if (_sections is null)
            throw PlcInterfaceEditException.Unsupported("The document has no AttributeList/Interface/Sections element.");
        if (_sections.Name.NamespaceName != InterfaceNamespaceV5)
            throw PlcInterfaceEditException.Unsupported($"The interface namespace '{_sections.Name.NamespaceName}' is not the evidenced '{InterfaceNamespaceV5}'.");
    }

    private XElement RequireSection(string name)
    {
        var sections = _sections!.Elements(_sections.Name.Namespace + "Section").ToList();
        return sections.FirstOrDefault(s => string.Equals((string?)s.Attribute("Name"), name, StringComparison.Ordinal))
            ?? sections.FirstOrDefault(s => string.Equals((string?)s.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase))
            ?? throw PlcInterfaceEditException.NotFound(
                $"Section '{name}' is not in the interface of '{ObjectName}'. Sections in the document: {string.Join(", ", sections.Select(s => (string?)s.Attribute("Name")))}.");
    }

    private void RefuseSystemSection(string sectionName, string action)
    {
        if (DocumentElement == "SW.Blocks.OB" && sectionName == "Input")
            throw PlcInterfaceEditException.Unsupported($"The Input section of an OB is defined by its event class; cannot {action} it.");
    }

    private XElement ResolveMember(XElement section, string sectionName, PlcMemberPath path)
    {
        var current = section;
        var walked = new List<string>();
        foreach (var segment in path.Segments)
        {
            walked.Add(segment.Name);
            current = FindChild(current, segment.Name, exactOnly: false)
                ?? throw PlcInterfaceEditException.NotFound($"Member '{sectionName}.{PlcMemberPath.FromNames(walked)}' was not found in '{ObjectName}'.");
        }

        return current;
    }

    private static XElement? FindChild(XElement container, string name, bool exactOnly)
    {
        var members = container.Elements(container.Name.Namespace + "Member").ToList();
        var exact = members.Where(m => string.Equals((string?)m.Attribute("Name"), name, StringComparison.Ordinal)).ToList();
        if (exact.Count == 1 || exactOnly) return exact.FirstOrDefault();
        var folded = members.Where(m => string.Equals((string?)m.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (folded.Count > 1) throw PlcInterfaceEditException.Invalid($"Member name '{name}' matches {folded.Count} members that differ only in case; use the exact name.");
        return folded.FirstOrDefault();
    }

    private static void RefuseInformative(XElement member, string sectionName, PlcMemberPath path)
    {
        if (string.Equals((string?)member.Attribute("Informative"), "true", StringComparison.OrdinalIgnoreCase))
            throw PlcInterfaceEditException.Unsupported($"'{sectionName}.{path}' is a system-defined (Informative) member and cannot be edited.");
    }

    private static void RefuseUnmodeledChildren(XElement member, string sectionName, PlcMemberPath path, string action)
    {
        var other = member.Elements().Where(e => !ModeledMemberChildren.Contains(e.Name.LocalName)).Select(e => e.Name.LocalName).Distinct().ToArray();
        if (other.Length > 0)
            throw PlcInterfaceEditException.Unsupported(
                $"'{sectionName}.{path}' contains elements this model does not order ({string.Join(", ", other)}); cannot {action} it. Use update_block_logic or update_type_content.");
    }

    /// <summary>
    /// A data type change must not leave content of the old type behind: elements this model does not
    /// order (nested <c>Sections</c> of a multi-instance, <c>Subelement</c> start values, anything
    /// else) and a <c>Version</c> attribute are refused, and an existing start value must be replaced
    /// (<c>startValue</c>) or removed (<c>clearStartValue</c>) explicitly instead of being kept or
    /// dropped silently.
    /// </summary>
    private static void RefuseStaleTypeContent(XElement member, XElement? startValue, PlcInterfaceEditRequest edit, string sectionName,
        string? currentType, string? resultingType)
    {
        var other = member.Elements().Where(e => !ModeledMemberChildren.Contains(e.Name.LocalName)).Select(e => e.Name.LocalName).Distinct().ToArray();
        if (other.Length > 0)
            throw PlcInterfaceEditException.Unsupported(
                $"'{sectionName}.{edit.Path}' carries {string.Join(", ", other)} of its data type '{currentType}', which would not fit '{resultingType}'; change it with update_block_logic or update_type_content.");
        if (member.Attribute("Version") is { } version)
            throw PlcInterfaceEditException.Unsupported(
                $"'{sectionName}.{edit.Path}' carries Version '{version.Value}' of its data type '{currentType}'; change it with update_block_logic or update_type_content.");
        if (startValue is not null && edit.StartValue is null && !edit.ClearStartValue)
            throw PlcInterfaceEditException.Invalid(
                $"'{sectionName}.{edit.Path}' has the start value '{startValue.Value}' of its data type '{currentType}'; changing the data type to '{resultingType}' needs startValue (a literal of the new type) or clearStartValue:true.");
    }

    private static void RequireNewName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length != name.Length || name.IndexOfAny(new[] { '"', '.', '[', ']' }) >= 0 || name.Any(char.IsControl))
            throw PlcInterfaceEditException.Invalid($"Member name '{name}' is not accepted: it must be nonblank, without surrounding blanks, control characters, '\"', '.', '[' or ']'.");
    }

    private static string RequireDataType(string? dataType)
    {
        if (dataType is null || string.IsNullOrWhiteSpace(dataType) || dataType.Any(char.IsControl))
            throw PlcInterfaceEditException.Invalid("dataType must be nonblank text without control characters, e.g. Int, \"MyUdt\" or Array[0..9] of Real.");
        return dataType.Trim();
    }

    private static void RequireCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture) || culture!.Any(c => !(char.IsLetterOrDigit(c) || c == '-')))
            throw PlcInterfaceEditException.Invalid("commentCulture is required with comment, e.g. en-US or de-DE.");
    }

    private static void RequireScalarForStartValue(string? dataType, string sectionName, PlcMemberPath path)
    {
        if (IsStruct(dataType) || IsArray(dataType) || IsQuoted(dataType))
            throw PlcInterfaceEditException.Unsupported(
                $"'{sectionName}.{path}' has data type '{dataType}'; only elementary members carry a StartValue element in this model. Set element and nested values of a DB with update_db_member.");
    }

    public static bool IsStruct(string? dataType) => string.Equals(dataType?.Trim(), "Struct", StringComparison.OrdinalIgnoreCase);

    public static bool IsArray(string? dataType) => dataType?.TrimStart().StartsWith("Array", StringComparison.OrdinalIgnoreCase) == true
        && dataType.TrimStart().Substring(5).TrimStart().StartsWith("[", StringComparison.Ordinal);

    public static bool IsQuoted(string? dataType)
    {
        var trimmed = dataType?.Trim();
        return trimmed is { Length: >= 2 } && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"';
    }

    private static List<PlcInterfaceMemberInfo> ReadMembers(XElement container, string? parentPath)
        => container.Elements(container.Name.Namespace + "Member").Select(member =>
        {
            var name = (string?)member.Attribute("Name") ?? string.Empty;
            var path = parentPath is null ? PlcMemberPath.FromNames(new[] { name }).ToString() : parentPath + "." + PlcMemberPath.FromNames(new[] { name });
            var ns = member.Name.Namespace;
            return new PlcInterfaceMemberInfo
            {
                Name = name,
                Path = path,
                DataType = (string?)member.Attribute("Datatype"),
                StartValue = member.Element(ns + "StartValue")?.Value,
                Informative = string.Equals((string?)member.Attribute("Informative"), "true", StringComparison.OrdinalIgnoreCase),
                Comments = member.Element(ns + "Comment")?.Elements(ns + "MultiLanguageText")
                    .Select(text => new PlcInterfaceCommentInfo { Culture = (string?)text.Attribute("Lang") ?? string.Empty, Text = text.Value }).ToList()
                    ?? new List<PlcInterfaceCommentInfo>(),
                Attributes = member.Attributes()
                    .Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName is not ("Name" or "Datatype" or "Informative"))
                    .Select(a => new PlcXmlAttributeInfo { Name = a.Name.LocalName, Value = a.Value }).ToList(),
                OtherElements = member.Elements().Where(e => !ModeledMemberChildren.Contains(e.Name.LocalName))
                    .Select(e => e.Name.LocalName).Distinct().ToList(),
                Members = ReadMembers(member, path),
            };
        }).ToList();

    // ---- whitespace-preserving tree edits -------------------------------------------------

    /// <summary>Inserts a member before <paramref name="before"/>, after the last sibling member, or as the container's first content.</summary>
    private void InsertMember(XElement container, XElement member, XElement? before)
    {
        var anchor = before ?? container.Elements(container.Name.Namespace + "Member").LastOrDefault();
        if (anchor is not null)
        {
            var indent = IndentOf(anchor);
            Format(member, indent);
            if (before is not null)
            {
                before.AddBeforeSelf(member);
                if (indent is not null) before.AddBeforeSelf(new XText("\n" + indent));
            }
            else
            {
                if (indent is not null) anchor.AddAfterSelf(new XText("\n" + indent), member);
                else anchor.AddAfterSelf(member);
            }

            return;
        }

        AppendChild(container, member);
    }

    /// <summary>Adds <paramref name="child"/> as the last child element, indented one level deeper than <paramref name="parent"/>.</summary>
    private void AppendChild(XElement parent, XElement child)
    {
        var last = parent.Elements().LastOrDefault();
        if (last is not null)
        {
            var indent = IndentOf(last);
            Format(child, indent);
            if (indent is not null) last.AddAfterSelf(new XText("\n" + indent), child);
            else last.AddAfterSelf(child);
            return;
        }

        var parentIndent = IndentOf(parent);
        if (parentIndent is null)
        {
            parent.Add(child);
            return;
        }

        var childIndent = parentIndent + IndentUnit();
        Format(child, childIndent);
        // An element without child elements has at most whitespace text; replace it with the indented child.
        foreach (var node in parent.Nodes().OfType<XText>().ToList()) node.Remove();
        parent.Add(new XText("\n" + childIndent), child, new XText("\n" + parentIndent));
    }

    private void PrependChild(XElement parent, XElement child)
    {
        var first = parent.Elements().FirstOrDefault();
        if (first is null)
        {
            AppendChild(parent, child);
            return;
        }

        var indent = IndentOf(first);
        Format(child, indent);
        first.AddBeforeSelf(child);
        if (indent is not null) first.AddBeforeSelf(new XText("\n" + indent));
    }

    /// <summary>Removes an element and the indentation text that precedes it.</summary>
    private static void Remove(XElement element)
    {
        if (element.PreviousNode is XText text && string.IsNullOrWhiteSpace(text.Value)) text.Remove();
        var parent = element.Parent;
        element.Remove();
        if (parent is not null && !parent.Elements().Any() && parent.Nodes().All(n => n is XText t && string.IsNullOrWhiteSpace(t.Value)))
            parent.RemoveNodes();
    }

    /// <summary>The indentation of an element that starts its own line, or null in a compact document.</summary>
    private static string? IndentOf(XElement element)
    {
        if (element.PreviousNode is not XText text) return null;
        var value = text.Value;
        var lineBreak = value.LastIndexOf('\n');
        if (lineBreak < 0) return null;
        var indent = value.Substring(lineBreak + 1);
        return indent.All(c => c == ' ' || c == '\t') ? indent : null;
    }

    private string IndentUnit()
    {
        if (_sections is not null && IndentOf(_sections) is { } outer
            && _sections.Elements().FirstOrDefault() is { } first && IndentOf(first) is { } inner && inner.Length > outer.Length && inner.StartsWith(outer, StringComparison.Ordinal))
            return inner.Substring(outer.Length);
        return "  ";
    }

    /// <summary>Indents the element's own child elements one level below <paramref name="indent"/>; text-only elements stay inline.</summary>
    private void Format(XElement element, string? indent)
    {
        if (indent is null || !element.Elements().Any()) return;
        var childIndent = indent + IndentUnit();
        foreach (var child in element.Elements().ToList())
        {
            child.AddBeforeSelf(new XText("\n" + childIndent));
            Format(child, childIndent);
        }

        element.Add(new XText("\n" + indent));
    }

    private static int FindRootStart(string xml)
    {
        var position = 0;
        while (position < xml.Length)
        {
            var c = xml[position];
            if (c == '﻿' || char.IsWhiteSpace(c))
            {
                position++;
                continue;
            }

            if (c != '<') return -1;
            if (StartsAt(xml, position, "<?")) position = Skip(xml, position, "?>");
            else if (StartsAt(xml, position, "<!--")) position = Skip(xml, position, "-->");
            else if (StartsAt(xml, position, "<!")) position = Skip(xml, position, ">");
            else return position;
            if (position < 0) return -1;
        }

        return -1;
    }

    private static bool StartsAt(string text, int index, string value)
        => string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

    private static int Skip(string text, int index, string terminator)
    {
        var end = text.IndexOf(terminator, index, StringComparison.Ordinal);
        return end < 0 ? -1 : end + terminator.Length;
    }
}
