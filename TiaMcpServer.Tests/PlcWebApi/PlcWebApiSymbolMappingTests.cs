using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.PlcWebApi;
using Xunit;

namespace TiaMcpServer.Tests.PlcWebApi;

/// <summary>The evidence-only mapping of offline table entries to Web API symbolic names.</summary>
public sealed class PlcWebApiSymbolMappingTests
{
    private static readonly IReadOnlyDictionary<string, string> Tags = PlcWebApiSymbolMapping.TagNames(new PlcTagInventoryPlcInfo
    {
        PlcName = "PLC_1",
        Tables =
        {
            new TagTableInfo { Name = "Default tag table", Tags = { new TagInfo { Name = "Motor_On" }, new TagInfo { Name = "Start Button" } } },
            new TagTableInfo { Name = "Motors", Tags = { new TagInfo { Name = "Speed" } } },
        },
    });

    [Theory]
    [InlineData("Motor_On", "\"Motor_On\"", "plcTag")]
    [InlineData("\"motor_on\"", "\"Motor_On\"", "plcTag")]
    [InlineData(" Speed ", "\"Speed\"", "plcTag")]
    [InlineData("\"Start Button\"", "\"Start Button\"", "plcTag")]
    [InlineData("\"DB_Line\".Speed", "\"DB_Line\".Speed", "webApiNotation")]
    [InlineData("\"DataTypes\".\"Bool\"", "\"DataTypes\".\"Bool\"", "webApiNotation")]
    [InlineData("\"DataTypes\".\"TIA_Dates\".\"Date_And_Time\"", "\"DataTypes\".\"TIA_Dates\".\"Date_And_Time\"", "webApiNotation")]
    [InlineData("\"DB\".\"Arr\"[1,-2]", "\"DB\".\"Arr\"[1,-2]", "webApiNotation")]
    [InlineData("\"Other_Tag\"", "\"Other_Tag\"", "webApiNotation")]
    public void MapsOnlyByDocumentedRules(string name, string webApiName, string rule)
    {
        var mapping = PlcWebApiSymbolMapping.MapWatchEntry(name, Tags);
        Assert.Equal((webApiName, rule, (string?)null), (mapping.WebApiName!, mapping.Rule!, mapping.UnmappedReason));
    }

    [Theory]
    [InlineData(null, "no symbolic name")]
    [InlineData("  ", "no symbolic name")]
    [InlineData("%MW10", "absolute address")]
    [InlineData("%DB1.DBX0.0", "absolute address")]
    [InlineData("DB_Line.Speed", "documented Web API notation")]
    [InlineData("\"DB_Line\".Struct.Speed", "documented Web API notation")]
    [InlineData("\"DB_Line\".Arr[1]", "documented Web API notation")]
    [InlineData("\"DB\"[1].Speed", "documented Web API notation")]
    [InlineData("#Local", "documented Web API notation")]
    [InlineData("\"Odd\"Name\"", "documented Web API notation")]
    [InlineData("\"DB\".\"A\"[x]", "documented Web API notation")]
    public void EverythingElseIsUnmappedWithAReason(string? name, string reason)
    {
        var mapping = PlcWebApiSymbolMapping.MapWatchEntry(name, Tags);
        Assert.Null(mapping.WebApiName);
        Assert.Null(mapping.Rule);
        Assert.Contains(reason, mapping.UnmappedReason);
    }

    [Theory]
    [InlineData("Start", "\"Start\"")]
    [InlineData("Start Button", "\"Start Button\"")]
    public void TagTableTagsAreQuotedRootVariables(string tag, string expected)
        => Assert.Equal((expected, "tagTable"), (PlcWebApiSymbolMapping.MapTag(tag).WebApiName!, PlcWebApiSymbolMapping.MapTag(tag).Rule!));

    [Theory]
    [InlineData("Odd\"Name")]
    [InlineData("")]
    [InlineData("Tab\tName")]
    public void TagNamesWithoutADocumentedQuotingAreUnmapped(string tag)
    {
        var mapping = PlcWebApiSymbolMapping.MapTag(tag);
        Assert.Null(mapping.WebApiName);
        Assert.NotNull(mapping.UnmappedReason);
    }

    [Fact]
    public void ReadMethodIsAnObserveWebApiMethod()
    {
        Assert.Contains(TiaMcpServer.Contracts.PlcWebApi.PlcWebApiMethods.ReadWatchTableLive, TiaMcpServer.Contracts.PlcWebApi.PlcWebApiMethods.Read);
        Assert.Equal(TiaMcpServer.Contracts.PlcWebApi.PlcWebApiMethods.ReadWatchTableLive, PlcWebApiCatalog.Method(PlcWebApiTableReader.Operation));
        Assert.Contains(PlcWebApiTableReader.Operation, PlcWebApiCatalog.ReadOperationNames);
        Assert.True(TiaMcpServer.Contracts.Safety.OperationPolicyCatalog.IsAllowed(TiaMcpServer.Contracts.Safety.McpAccessMode.ReadOnly,
            TiaMcpServer.Contracts.PlcWebApi.PlcWebApiMethods.ReadWatchTableLive));
    }
}
