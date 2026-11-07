using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

/// <summary>Pins the "Claude in TIA" Add-In's compile-only stub surface and its Publisher configuration (offline; not live-tested).</summary>
public sealed class ClaudeInTiaAddInContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    /// <summary>Every type the stub declares; each has a provenance row in docs/SupportedOperations/CLAUDE_IN_TIA.md and is a
    /// documented V21 type (T: entry of Siemens.Engineering.AddIn.Base.xml). A new type needs both.</summary>
    private static readonly string[] StubTypes =
    {
        "Siemens.Engineering.AddIn.Menu.MenuItem", "Siemens.Engineering.AddIn.Menu.ContextMenuAddIn", "Siemens.Engineering.AddIn.Menu.ContextMenuAddInRoot",
        "Siemens.Engineering.AddIn.Menu.ChildItemFactory", "Siemens.Engineering.AddIn.Menu.ActionItem", "Siemens.Engineering.AddIn.Menu.ActionItem`1",
        "Siemens.Engineering.AddIn.Menu.MenuSelectionProvider", "Siemens.Engineering.AddIn.Menu.MenuSelectionProvider`1",
        "Siemens.Engineering.AddIn.ProjectTreeAddInProvider", "Siemens.Engineering.AddIn.DevicesAndNetworksAddInProvider",
        "Siemens.Engineering.AddIn.ProjectLibraryTreeAddInProvider", "Siemens.Engineering.AddIn.GlobalLibraryTreeAddInProvider",
        "Siemens.Engineering.AddIn.MessageBoxProvider", "Siemens.Engineering.AddIn.FeedbackProvider",
    };

    [Fact]
    public void Stub_DeclaresExactlyThePinnedTypes_AndEachHasAProvenanceRow()
    {
        var source = File.ReadAllText(Path.Combine(Root, "reference-stubs", "Siemens.Engineering.AddIn.Base", "AddIn.cs"));
        var declared = new List<string>();
        foreach (Match block in Regex.Matches(source, @"namespace (?<ns>[\w.]+)\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline))
            foreach (Match type in Regex.Matches(block.Groups["body"].Value, @"\n    public (?:abstract |sealed )*(?:class|delegate void) (?<name>\w+)(?<generic><\w+>)?"))
                declared.Add(block.Groups["ns"].Value + "." + type.Groups["name"].Value + (type.Groups["generic"].Success ? "`1" : ""));
        Assert.Equal(StubTypes.Order(StringComparer.Ordinal), declared.Order(StringComparer.Ordinal));

        var reference = File.ReadAllText(Path.Combine(Root, "docs", "SupportedOperations", "CLAUDE_IN_TIA.md"));
        foreach (var type in StubTypes)
            Assert.Contains("`" + type.Split('.').Last().Replace("`1", "<T>") + "`", reference, StringComparison.Ordinal);
    }

    [Fact]
    public void Stub_IsNotPartOfTheHashedReferenceSet()
    {
        var verifier = File.ReadAllText(Path.Combine(Root, "scripts", "verify-reference-stubs.ps1"));
        Assert.DoesNotContain("AddIn", verifier, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Root, "ref", "Siemens.Engineering.AddIn.Base.dll")));
    }

    [Fact]
    public void PublisherConfig_RequestsReadOnlyTiaAccessAndFileAccessOnly()
    {
        var document = XDocument.Load(Path.Combine(Root, "TiaMcpServer.AddIn", "AddInPublisherConfig.xml"));
        var ns = document.Root!.Name.Namespace;
        Assert.Equal("PackageConfiguration", document.Root.Name.LocalName);
        Assert.Equal("TiaMcpServer.AddIn.dll", document.Descendants(ns + "Assembly").Single().Value);
        var tia = document.Descendants(ns + "TIAPermissions").Single().Elements().Select(e => e.Name.LocalName).ToArray();
        Assert.Equal(new[] { "TIA.ReadOnly" }, tia);
        var security = document.Descendants(ns + "SecurityPermissions").Single().Elements().Select(e => e.Name.LocalName).ToArray();
        Assert.Equal(new[] { "System.Security.Permissions.FileIOPermission-UNRESTRICTED" }, security);
        Assert.Empty(document.Descendants(ns + "UnrestrictedPermissions"));
    }

    [Fact]
    public void AddIn_TouchesOnlyTheInboxStore()
    {
        var source = File.ReadAllText(Path.Combine(Root, "TiaMcpServer.AddIn", "ClaudeAddIn.cs"));
        Assert.DoesNotContain("File.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Export", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetAttribute", source, StringComparison.Ordinal);
        Assert.Contains("ClaudeInboxStore", source, StringComparison.Ordinal);
    }
}
