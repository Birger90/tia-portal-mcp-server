// Origin: BetterTIA/watch, src/BtiaWatch/WatchTools.cs (FormatSnapshot, IsAtOrBelow, TopLevel),
// commit 1978f24b330f265d8ed2a67e9b1d673d1b4122d8, MIT License, Copyright (c) 2026 HonzaT.
// Adapted: returns typed values instead of text lines, adds a value cap; see THIRD_PARTY_NOTICES.md.
using System.Xml;
using System.Xml.Linq;

namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>
/// Reads the export of a DB's interface snapshot (<c>InterfaceSnapshot.Export(FileInfo, ExportOptions.WithReadOnly)</c>):
/// the read-only attribute <c>Snapshot</c> holds one <c>Value</c> element per member with its <c>Path</c> (already in the
/// form <c>A.B[2].C</c>) and <c>Type</c>, and <c>SnapshotDate</c> the time the snapshot was taken. The layout is the one
/// BetterTIA/watch reports from a V21 live check (<c>SW.Interface.Snapshot.xsd</c>); nothing else of the document is
/// interpreted. Siemens-free; shared by the worker and the FakeWorker.
/// </summary>
public static class PlcDbSnapshotDocument
{
    public const int DefaultMaxValues = 2000;
    public const int MaxValues = 20000;

    public sealed class Reading
    {
        /// <summary>False when the export has no <c>Snapshot</c> attribute (no snapshot stored, or a layout this reader does not know).</summary>
        public bool HasSnapshot { get; set; }

        public string? SnapshotDate { get; set; }

        public int TotalValues { get; set; }

        public int MatchedValues { get; set; }

        public bool Truncated { get; set; }

        public List<PlcDbSnapshotValueInfo> Values { get; } = new();

        /// <summary>The top-level member names, listed when a member filter matched nothing (at most 50).</summary>
        public List<string> TopLevelMembers { get; } = new();
    }

    /// <exception cref="ArgumentException">The export is not XML.</exception>
    public static Reading Read(string xml, string? memberPath, int maxValues)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException exception)
        {
            throw new ArgumentException($"The snapshot export is not XML: {exception.Message}");
        }

        var reading = new Reading();
        var attributes = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "AttributeList");
        var snapshot = attributes?.Elements().FirstOrDefault(e => e.Name.LocalName == "Snapshot");
        if (snapshot is null) return reading;
        reading.HasSnapshot = true;
        reading.SnapshotDate = attributes!.Elements().FirstOrDefault(e => e.Name.LocalName == "SnapshotDate")?.Value;
        var values = snapshot.Descendants().Where(e => e.Name.LocalName == "Value").ToList();
        reading.TotalValues = values.Count;
        var filter = memberPath?.Trim();
        var matched = string.IsNullOrEmpty(filter) ? values : values.Where(v => IsAtOrBelow(PathOf(v), filter!)).ToList();
        reading.MatchedValues = matched.Count;
        foreach (var value in matched.Take(maxValues))
            reading.Values.Add(new PlcDbSnapshotValueInfo { Path = PathOf(value), Value = value.Value, DataType = (string?)value.Attribute("Type") });
        reading.Truncated = matched.Count > maxValues;
        if (matched.Count == 0 && values.Count > 0)
            reading.TopLevelMembers.AddRange(values.Select(v => TopLevel(PathOf(v))).Distinct(StringComparer.OrdinalIgnoreCase).Take(50));
        return reading;
    }

    /// <summary>True when <paramref name="path"/> is the member itself or lies below it ('A.B' matches 'A.B', 'A.B.C' and 'A.B[1]', not 'A.BC'); case-insensitive.</summary>
    public static bool IsAtOrBelow(string path, string member)
    {
        member = member.Trim().TrimEnd('.');
        if (member.Length == 0 || !path.StartsWith(member, StringComparison.OrdinalIgnoreCase)) return false;
        return path.Length == member.Length || path[member.Length] == '.' || path[member.Length] == '[';
    }

    private static string PathOf(XElement value) => (string?)value.Attribute("Path") ?? string.Empty;

    private static string TopLevel(string path)
    {
        var end = path.IndexOfAny(new[] { '.', '[' });
        return end < 0 ? path : path.Substring(0, end);
    }
}

/// <summary>One member of a DB snapshot: its path, the actual value as TIA Portal wrote it and its data type.</summary>
public class PlcDbSnapshotValueInfo
{
    public string Path { get; set; } = string.Empty;

    public string? Value { get; set; }

    public string? DataType { get; set; }
}

/// <summary>
/// Result of get_db_snapshot (the snapshot stored in the offline project) and create_db_snapshot (a new snapshot of the
/// actual values from the online PLC, which TIA Portal stores in the offline project).
/// </summary>
public class PlcDbSnapshotInfo
{
    public string Operation { get; set; } = string.Empty;

    public PlcBlockIdentityInfo Block { get; set; } = new PlcBlockIdentityInfo();

    /// <summary>OnlineProvider.State read before CreateSnapshot (create_db_snapshot); null for get_db_snapshot.</summary>
    public string? OnlineState { get; set; }

    /// <summary>The export carries a Snapshot attribute; false means no snapshot is stored (or an unknown layout).</summary>
    public bool HasSnapshot { get; set; }

    /// <summary>SnapshotDate as exported (the time the snapshot was taken); null when absent.</summary>
    public string? SnapshotDate { get; set; }

    /// <summary>The member filter applied (the member itself and everything below it); null for every member.</summary>
    public string? MemberPath { get; set; }

    public int TotalValues { get; set; }

    public int MatchedValues { get; set; }

    public bool Truncated { get; set; }

    public List<PlcDbSnapshotValueInfo> Values { get; set; } = new List<PlcDbSnapshotValueInfo>();

    public List<string> TopLevelMembers { get; set; } = new List<string>();

    public List<string> Messages { get; set; } = new List<string>();
}
