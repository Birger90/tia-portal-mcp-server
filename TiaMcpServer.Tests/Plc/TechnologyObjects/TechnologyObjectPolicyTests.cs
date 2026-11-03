using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using Xunit;
using static TiaMcpServer.Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy;

namespace TiaMcpServer.Tests.Plc.TechnologyObjects;

public sealed class TechnologyObjectPolicyTests
{
    [Theory]
    [InlineData(@"C:\Exports\Axis_1.xml")]
    [InlineData("d:/exports/axis.xml")]
    [InlineData(@"C:\a.b\Axis 1.xml")]
    [InlineData(@"C:\Exports\CONSOLE.xml")]
    [InlineData(@"C:\Exports\COM10.xml")]
    [InlineData(@"C:\Exports\LPT.xml")]
    [InlineData(@"C:\Exports\NULL\x.xml")]
    [InlineData(@"C:\Exports\.hidden.xml")]
    // UNC shares are accepted since 2026-11-01 (owner decision); device paths stay refused.
    [InlineData(@"\\server\share\Axis_1.xml")]
    [InlineData("//server/share/Axis_1.xml")]
    public void LocalDrivePathsAreAccepted(string path) => Assert.Null(ValidateLocalFilePath(path));

    [Theory]
    [InlineData(@"\\?\C:\Exports\Axis_1.xml", "UNC")]
    [InlineData(@"\\.\C:\Exports\Axis_1.xml", "UNC")]
    [InlineData(@"\??\C:\Exports\Axis_1.xml", "UNC")]
    [InlineData(@"Exports\Axis_1.xml", "absolute local file path")]
    [InlineData("/tmp/axis.xml", "absolute local file path")]
    [InlineData(@"C:Axis_1.xml", "absolute local file path")]
    [InlineData(@"C:\Exports\", "must name a file")]
    [InlineData(@"C:\Exports\Axis_1.xml:stream", "after the drive")]
    [InlineData(@"C:\Exports\Ax*s.xml", "after the drive")]
    [InlineData("C:\\Exports\\Axis\u0007.xml", "control")]
    [InlineData("C:\\Exports\\Axis\u200B.xml", "format")]
    [InlineData(@"C:\Exports\\Axis.xml", "empty path segment")]
    [InlineData("", "required")]
    [InlineData(@"C:\a\..\b\Axis 1.xml", "'.' or '..'")]
    [InlineData(@"C:\a\.\Axis.xml", "'.' or '..'")]
    [InlineData(@"C:\Exports\..", "'.' or '..'")]
    [InlineData(@"C:\Exports.\Axis.xml", "ends with a dot or a space")]
    [InlineData(@"C:\Exports \Axis.xml", "ends with a dot or a space")]
    [InlineData(@"C:\Exports\Axis.xml.", "ends with a dot or a space")]
    [InlineData(@"C:\Exports\Axis.xml ", "ends with a dot or a space")]
    [InlineData(@"C:\Exports\CON", "reserved Windows device name")]
    [InlineData(@"C:\Exports\nul.txt", "reserved Windows device name")]
    [InlineData(@"C:\Exports\Aux.tar.gz", "reserved Windows device name")]
    [InlineData(@"C:\Exports\con .xml", "reserved Windows device name")]
    [InlineData(@"C:\PRN\Axis.xml", "reserved Windows device name")]
    [InlineData(@"C:\Exports\COM0.xml", "reserved Windows device name")]
    [InlineData(@"C:\Exports\com9", "reserved Windows device name")]
    [InlineData(@"C:\Exports\LPT1.log", "reserved Windows device name")]
    [InlineData("C:\\Exports\\COM\u00B9.xml", "reserved Windows device name")]
    [InlineData("C:\\Exports\\lpt\u00B3", "reserved Windows device name")]
    [InlineData(@"C:\Exports\CONIN$", "reserved Windows device name")]
    [InlineData(@"C:\Exports\conout$.txt", "reserved Windows device name")]
    [InlineData(@"C:\CLOCK$\Axis.xml", "reserved Windows device name")]
    public void UncDevicePathsAndMalformedPathsAreRefused(string path, string expected)
    {
        var refusal = ValidateLocalFilePath(path);
        Assert.NotNull(refusal);
        Assert.Contains(expected, refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("10.0", 10, 0)]
    [InlineData("6.0.1", 6, 0)]
    [InlineData("8.0.0.2", 8, 0)]
    public void VersionsParseWithSystemVersionRules(string text, int major, int minor)
    {
        var version = ParseVersion(text);
        Assert.NotNull(version);
        Assert.Equal(major, version!.Major);
        Assert.Equal(minor, version.Minor);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("V10.0")]
    [InlineData(" 10.0")]
    [InlineData("10.0 ")]
    [InlineData("10.a")]
    [InlineData("")]
    [InlineData(null)]
    public void MalformedVersionsAreRefused(string? text) => Assert.Null(ParseVersion(text));

    [Fact]
    public void ConnectOverloadMatrixFollowsTheV21Reference()
    {
        Assert.Equal(new[] { "address", "module", "modules", "channel", "tag", "dbMember" }, KindsFor(Interfaces.Actor));
        Assert.Equal(KindsFor(Interfaces.Actor), KindsFor(Interfaces.Sensor));
        Assert.Equal(new[] { "address", "module", "modules", "dbMember" }, KindsFor(Interfaces.Torque));
        Assert.Equal(new[] { "address", "module", "channel" }, KindsFor(Interfaces.MeasuringInput));
        Assert.Equal(new[] { "address", "channel", "tag" }, KindsFor(Interfaces.OutputCam));
        Assert.Equal(new[] { "module" }, KindsFor(Interfaces.Ident));
        Assert.Empty(KindsFor("encoder"));
        Assert.False(HasDisconnect(Interfaces.Ident));
        Assert.All(Interfaces.All.Where(i => i != Interfaces.Ident), i => Assert.True(HasDisconnect(i)));
        Assert.Contains("SensorIndexInActorTelegram", PropertiesFor(Interfaces.Sensor));
        Assert.DoesNotContain("Channel", PropertiesFor(Interfaces.Torque));
        Assert.Equal(new[] { "ConnectedIdentDevice" }, PropertiesFor(Interfaces.Ident));
    }

    [Fact]
    public void GroupPathsNormalizeLikeTheOtherPlcGroupPaths()
    {
        Assert.Equal("/", NormalizeGroupPath(null));
        Assert.Equal("/", NormalizeGroupPath(" / "));
        Assert.Equal("/Axes/Line1", NormalizeGroupPath("Axes//Line1/"));
        Assert.Equal("/Axes", ParentPath("/Axes/Line1"));
        Assert.Equal("/", ParentPath("/Axes"));
        Assert.Equal("/Axes/X", Combine("/Axes", "X"));
        Assert.Equal("/X", Combine("/", "X"));
    }

    private const string Export = "<Document><Engineering version=\"V21\" /><SW.TechnologicalObjects.TechnologicalInstanceDB ID=\"0\"><AttributeList><Name>Axis_1</Name></AttributeList></SW.TechnologicalObjects.TechnologicalInstanceDB></Document>";

    [Fact]
    public void ImportDocumentMustDescribeExactlyTheAddressedObjectLikeTheExport()
    {
        Assert.Null(CheckImportDocument(Export, Export.Replace("ID=\"0\"", "ID=\"1\"", StringComparison.Ordinal), "Axis_1"));
        Assert.Contains("names 'Axis_2'", CheckImportDocument(Export, Export.Replace("Axis_1", "Axis_2", StringComparison.Ordinal), "Axis_1"));
        Assert.Contains("well-formed", CheckImportDocument(Export, "<Document>", "Axis_1"));
        Assert.Contains("well-formed", CheckImportDocument(Export, "<!DOCTYPE x [<!ENTITY e \"x\">]><Document />", "Axis_1"));
        Assert.Contains("root element", CheckImportDocument(Export, "<Other />", "Axis_1"));
        var two = Export.Replace("</Document>", "<SW.TechnologicalObjects.TechnologicalInstanceDB ID=\"9\"><AttributeList><Name>Axis_1</Name></AttributeList></SW.TechnologicalObjects.TechnologicalInstanceDB></Document>", StringComparison.Ordinal);
        Assert.Contains("child elements", CheckImportDocument(Export, two, "Axis_1"));
        Assert.Contains("child elements", CheckImportDocument(Export, Export.Replace("SW.TechnologicalObjects.TechnologicalInstanceDB", "SW.Blocks.GlobalDB", StringComparison.Ordinal), "Axis_1"));
        Assert.Contains("exactly one object", CheckImportDocument(Export, Export.Replace("<Name>Axis_1</Name>", "<Title>Axis_1</Title>", StringComparison.Ordinal), "Axis_1"));
        // The root must have the export's children: an extra or missing sibling is refused; DocumentInfo is ignored.
        Assert.Contains("child elements", CheckImportDocument(Export, Export.Replace("<Engineering version=\"V21\" />", string.Empty, StringComparison.Ordinal), "Axis_1"));
        Assert.Contains("child elements", CheckImportDocument(Export, Export.Replace("</Document>", "<Extra /></Document>", StringComparison.Ordinal), "Axis_1"));
        Assert.Null(CheckImportDocument(Export, Export.Replace("<Document>", "<Document><DocumentInfo><Created>x</Created></DocumentInfo>", StringComparison.Ordinal), "Axis_1"));
        // The name is compared exactly, without trimming.
        Assert.Contains("names ' Axis_1'", CheckImportDocument(Export, Export.Replace("<Name>Axis_1</Name>", "<Name> Axis_1</Name>", StringComparison.Ordinal), "Axis_1"));
        Assert.Contains("no single recognisable", CheckImportDocument(Export.Replace("<Name>Axis_1</Name>", "<Name>Axis_1 </Name>", StringComparison.Ordinal), Export, "Axis_1"));
        // A second Name (or AttributeList) is ambiguous: refused in the content, fail closed in the template.
        var twoNames = Export.Replace("<Name>Axis_1</Name>", "<Name>Axis_1</Name><Name>Axis_2</Name>", StringComparison.Ordinal);
        Assert.Contains("exactly one AttributeList with exactly one Name", CheckImportDocument(Export, twoNames, "Axis_1"));
        Assert.Contains("no single recognisable", CheckImportDocument(twoNames, Export, "Axis_1"));
        // Root names are compared and shown as full XML names.
        var namespaced = Export.Replace("<Document>", "<Document xmlns=\"urn:other\">", StringComparison.Ordinal);
        Assert.Contains("'{urn:other}Document', but the SimaticML export of this object has 'Document'", CheckImportDocument(Export, namespaced, "Axis_1"));
        // A template that has no recognisable object element fails closed: nothing about the schema is assumed.
        Assert.Contains("no single recognisable", CheckImportDocument("<Document><X /></Document>", Export, "Axis_1"));
    }

    [Fact]
    public void MethodListsAreDisjointAndCoverEveryOperation()
    {
        Assert.Equal(16, TechnologyObjectMethods.All.Count);
        Assert.Empty(TechnologyObjectMethods.Read.Intersect(TechnologyObjectMethods.Write));
        Assert.Equal(TechnologyObjectMethods.All.Count, TechnologyObjectMethods.All.Distinct().Count());
        Assert.True(TechnologyObjectMethods.IsWrite(TechnologyObjectMethods.Export));
        Assert.False(TechnologyObjectMethods.IsRead(TechnologyObjectMethods.Export));
        Assert.Equal(new[] { TechnologyObjectMethods.GetContent }, TechnologyObjectMethods.TemporaryExport);
    }

    [Fact]
    public void AWriteConsultsOnlyTheProvidersItsInterfaceNeeds_K18()
    {
        Assert.Equal(new[] { SiemensNames.AxisProvider }, ProvidersFor(Interfaces.Actor));
        Assert.Equal(new[] { SiemensNames.AxisProvider }, ProvidersFor(Interfaces.Torque));
        Assert.Equal(new[] { SiemensNames.AxisProvider, SiemensNames.EncoderProvider }, ProvidersFor(Interfaces.Sensor));
        Assert.Equal(new[] { SiemensNames.MeasuringInputProvider }, ProvidersFor(Interfaces.MeasuringInput));
        Assert.Equal(new[] { SiemensNames.OutputCamProvider }, ProvidersFor(Interfaces.OutputCam));
        Assert.Equal(new[] { SiemensNames.IdentProvider }, ProvidersFor(Interfaces.Ident));
        Assert.Empty(ProvidersFor("nonsense"));
        // Every documented provider serves at least one interface.
        Assert.All(SiemensNames.Providers, p => Assert.Contains(Interfaces.All, i => ProvidersFor(i).Contains(p)));
    }

    [Fact]
    public void TheAxisSensorsWinOverTheEncoderProviderWhichIsOnlyTheAlternative()
    {
        var both = new List<(string, string, int?, string)>
        {
            (Interfaces.Sensor, SiemensNames.EncoderProvider, null, "encoder"),
            (Interfaces.Sensor, SiemensNames.AxisProvider, 0, "axis0"),
            (Interfaces.Sensor, SiemensNames.AxisProvider, 1, "axis1"),
            (Interfaces.Actor, SiemensNames.AxisProvider, null, "actor"),
        };
        Assert.Equal(new[] { "axis0", "axis1" }, PreferredEndpoints(both, Interfaces.Sensor).Select(e => e.Target));
        Assert.Equal(new[] { "encoder" }, PreferredEndpoints(both.Where(e => e.Item2 != SiemensNames.AxisProvider), Interfaces.Sensor).Select(e => e.Target));
        Assert.Equal(new[] { "actor" }, PreferredEndpoints(both, Interfaces.Actor).Select(e => e.Target));
        Assert.Empty(PreferredEndpoints(both, Interfaces.Torque));
        // An interface served by a provider that does not serve it per the documented map is never chosen.
        Assert.Empty(PreferredEndpoints(new[] { (Interfaces.Actor, SiemensNames.EncoderProvider, (int?)null, "x") }, Interfaces.Actor));
    }

    [Fact]
    public void AnUnavailableProviderFailsOnlyTheInterfacesThatNeedIt()
    {
        var encoder = new[] { TiaMcpServer.Contracts.Shared.ListedServices.UnavailableReason(SiemensNames.EncoderProvider) };
        Assert.Null(UnavailableProviderMessage(Interfaces.Actor, encoder));
        Assert.Null(UnavailableProviderMessage(Interfaces.Torque, encoder));
        var sensor = UnavailableProviderMessage(Interfaces.Sensor, encoder)!;
        Assert.Contains(encoder[0], sensor);
        Assert.EndsWith("Nothing was changed.", sensor, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sensor, "Nothing was changed"));
        Assert.Null(UnavailableProviderMessage(Interfaces.Sensor, Array.Empty<string>()));
    }
}
