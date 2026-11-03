using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts.Plc.SimaticSd;

/// <summary>One text of an .s7res entry in one culture.</summary>
public sealed record SimaticSdCultureText(string Culture, string Text);

/// <summary>
/// The .s7res resource document of a SIMATIC SD export: <c>MultiLingualTexts:</c> followed by entries
/// <c>- id: MLC_jr</c> with one <c>culture: text</c> line per language (the form of the V21 exports in
/// 1748477582/tia-portal-openness-mcp and HonzaT/BetterTIA). Plain, single- and double-quoted YAML scalars and
/// <c>|</c>/<c>&gt;</c> block scalars are read; anything else is kept as written. Read only: the network edits never
/// change the .s7res (§10 of the Siemens skill). Siemens-free.
/// </summary>
public static class SimaticSdResources
{
    private static readonly Regex EntryStart = new(@"^(?<indent>\s*)-\s+id\s*:\s*(?<id>.*?)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Field = new(@"^(?<indent>\s+)(?<key>[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*)\s*:\s?(?<value>.*)$", RegexOptions.CultureInvariant);

    /// <summary>The texts by id (ids compared case-sensitively, as written).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<SimaticSdCultureText>> Parse(string? text)
    {
        var result = new Dictionary<string, IReadOnlyList<SimaticSdCultureText>>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text)) return result;
        var lines = text!.TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        string? id = null;
        List<SimaticSdCultureText>? texts = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var start = EntryStart.Match(lines[i]);
            if (start.Success)
            {
                id = Scalar(start.Groups["id"].Value);
                texts = new List<SimaticSdCultureText>();
                result[id] = texts;
                continue;
            }

            if (texts is null) continue;
            var field = Field.Match(lines[i]);
            if (!field.Success) continue;
            var value = field.Groups["value"].Value;
            if (value.Trim() is "|" or ">" or "|-" or ">-" or "|+" or ">+")
            {
                var indent = field.Groups["indent"].Value.Length;
                var block = new List<string>();
                while (i + 1 < lines.Count && (lines[i + 1].Trim().Length == 0 || SimaticSdPragma.LeadingWhitespace(lines[i + 1]).Length > indent))
                    block.Add(lines[++i].Trim());
                texts.Add(new(field.Groups["key"].Value, string.Join(value.Trim().StartsWith(">", StringComparison.Ordinal) ? " " : "\n", block).Trim()));
                continue;
            }

            texts.Add(new(field.Groups["key"].Value, Scalar(value)));
        }

        return result;
    }

    private static string Scalar(string raw)
    {
        var value = raw.Trim();
        if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'') return value.Substring(1, value.Length - 2).Replace("''", "'");
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            return value.Substring(1, value.Length - 2).Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\");
        return value;
    }
}

/// <summary>
/// Instruction forms a V21 SIMATIC SD export writes that are reported (1748477582/tia-portal-openness-mcp skill §9a; not
/// reproduced live on V21 on 2026-11-02 for GT/EQ_Contact, Move, P_Trig/N_Trig, TON/TOF and Add) to fail ImportFromDocuments with a generic "Failed importing": <c>Gt{ ... }(</c>, <c>Lt{</c>, <c>Eq{</c>,
/// <c>Ne{</c>, <c>Ge{</c>, <c>Le{</c>, <c>PBox(</c>, <c>NBox(</c> and <c>Move{ ... }(</c>. The reported import forms are
/// <c>GT_Contact( in1 := ..., in2 := ... )</c> with <c>{ S7_Templates := "SrcType := Int" }</c>, <c>P_Trig( #mem )</c> and
/// <c>Move( in := ..., out1 => ... )</c>. Nothing is rewritten: no mapping is evidenced.
/// </summary>
public static class SimaticSdExportForms
{
    private static readonly Regex Braced = new(@"(?<![A-Za-z0-9_#.""])(?<form>Gt|Lt|Eq|Ne|Ge|Le|Move)\s*\{", RegexOptions.CultureInvariant);
    private static readonly Regex Boxed = new(@"(?<![A-Za-z0-9_#.""])(?<form>PBox|NBox)\s*\(", RegexOptions.CultureInvariant);

    /// <summary>The reported export-only forms in <paramref name="code"/>, each once, as <c>Gt{</c> or <c>PBox(</c>.</summary>
    public static IReadOnlyList<string> Find(string code)
        => Braced.Matches(code).Cast<Match>().Select(m => m.Groups["form"].Value + "{")
            .Concat(Boxed.Matches(code).Cast<Match>().Select(m => m.Groups["form"].Value + "("))
            .Distinct(StringComparer.Ordinal).ToList();
}
