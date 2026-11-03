using TiaMcpServer.Contracts.PlcOnline;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>Schema-free reads: watch/force table entries by documented attribute name, the Entries property by reflection, and result messages.</summary>
public sealed class PlcWatchTablePolicyTests
{
    private enum DisplayFormat
    {
        Bool,
        Hex,
    }

    private sealed class Table
    {
        public IEnumerable<object> Entries { get; } = new object[] { "first", "second" };
    }

    private sealed class TableWithoutEntries
    {
        public int Entries => 3;
    }

    [Fact]
    public void WatchEntry_ReadsTheDocumentedAttributesAsTypedText()
    {
        var values = new Dictionary<string, object?>
        {
            ["Name"] = "Motor_On", ["Address"] = "%Q0.0", ["DisplayFormat"] = DisplayFormat.Hex, ["MonitorTrigger"] = "Permanent",
            ["ModifyIntention"] = true, ["ModifyTrigger"] = 3, ["ModifyValue"] = "16#FF",
        };

        var entry = PlcWatchTablePolicy.WatchEntry(4, name => values.TryGetValue(name, out var value) ? (true, value) : (false, null));

        Assert.Equal(4, entry.Index);
        Assert.Equal("Motor_On", entry.Name);
        Assert.Equal("%Q0.0", entry.Address);
        Assert.Equal("Hex", entry.DisplayFormat);
        Assert.Equal("Permanent", entry.MonitorTrigger);
        Assert.True(entry.ModifyIntention);
        Assert.Equal("3", entry.ModifyTrigger);
        Assert.Equal("16#FF", entry.ModifyValue);
        Assert.Empty(entry.UnreadableAttributes);
    }

    [Fact]
    public void WatchEntry_RecordsUnreadableAndNonScalarAttributes()
    {
        var entry = PlcWatchTablePolicy.WatchEntry(0, name => name switch
        {
            "Name" => (true, "Speed"),
            "Address" => (true, new object()),
            "ModifyIntention" => (true, "yes"),
            "ModifyValue" => (true, null),
            _ => (false, null),
        });

        Assert.Equal("Speed", entry.Name);
        Assert.Null(entry.Address);
        Assert.Null(entry.ModifyIntention);
        Assert.Null(entry.ModifyValue);
        Assert.Equal(new[] { "Address", "DisplayFormat", "MonitorTrigger", "ModifyIntention", "ModifyTrigger" }, entry.UnreadableAttributes);
    }

    [Fact]
    public void ForceEntry_ReadsTheForceAttributes()
    {
        var entry = PlcWatchTablePolicy.ForceEntry(0, name => name switch
        {
            "Name" => (true, "Valve"), "Address" => (true, "%Q1.0"), "DisplayFormat" => (true, "Bool"), "MonitorTrigger" => (true, "Permanent"),
            "ForceIntention" => (true, false), "ForceValue" => (true, "FALSE"), _ => (false, null),
        });

        Assert.False(entry.ForceIntention);
        Assert.Equal("FALSE", entry.ForceValue);
        Assert.Empty(entry.UnreadableAttributes);
    }

    [Fact]
    public void Entries_IsFoundByReflectionOnlyWhenEnumerable()
    {
        Assert.Equal(new object[] { "first", "second" }, PlcWatchTablePolicy.Entries(new Table()));
        Assert.Null(PlcWatchTablePolicy.Entries(new TableWithoutEntries()));
        Assert.Null(PlcWatchTablePolicy.Entries(new object()));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("Line", "/Line")]
    [InlineData("/Line//Cell/", "/Line/Cell")]
    [InlineData(" /Line / Cell ", "/Line/Cell")]
    public void NormalizeGroupPath(string? path, string expected)
        => Assert.Equal(expected, PlcWatchTablePolicy.NormalizeGroupPath(path));

    [Fact]
    public void ScalarText_IsInvariantAndBounded()
    {
        Assert.Equal("1.5", PlcWatchTablePolicy.ScalarText(1.5));
        Assert.Equal("true", PlcWatchTablePolicy.ScalarText(true));
        Assert.Equal("Hex", PlcWatchTablePolicy.ScalarText(DisplayFormat.Hex));
        Assert.Equal(PlcOnlinePolicy.MaxTextChars, PlcWatchTablePolicy.ScalarText(new string('x', 5000))!.Length);
        Assert.Null(PlcWatchTablePolicy.ScalarText(new object()));
    }

    private sealed class Message(string text, params Message[] children)
    {
        public string Text { get; } = text;

        public IEnumerable<Message> Messages { get; } = children;

        public Message? Parent { get; } = null;
    }

    private static (List<PlcEngineeringMessageInfo> Messages, bool Complete, string? Omission) ProjectMessages(IEnumerable<Message> messages)
        => PlcEngineeringMessageProjector.Project(messages,
            message => new[] { "Text", "Broken", "Object" },
            (message, name) => name switch
            {
                "Text" => (true, ((Message)message).Text),
                "Object" => (true, new object()),
                _ => (false, null),
            },
            item => item is Message);

    [Fact]
    public void Messages_ProjectScalarAttributesAndNestedMessages()
    {
        var (messages, complete, omission) = ProjectMessages(new[] { new Message("Upload done", new Message("Hardware"), new Message("Software")) });

        Assert.True(complete);
        Assert.Null(omission);
        var top = Assert.Single(messages);
        var attribute = Assert.Single(top.Attributes);
        Assert.Equal("Text", attribute.Name);
        Assert.Equal("Upload done", attribute.Value);
        Assert.Equal(new[] { "Hardware", "Software" }, top.Messages.Select(m => m.Attributes.Single().Value));
    }

    [Fact]
    public void Messages_AreCappedInCountAndDepth()
    {
        var many = Enumerable.Range(0, PlcEngineeringMessageProjector.MaxMessages + 5).Select(i => new Message("m" + i));
        var (messages, complete, omission) = ProjectMessages(many);
        Assert.Equal(PlcEngineeringMessageProjector.MaxMessages, messages.Count);
        Assert.False(complete);
        Assert.Contains("omitted", omission);

        var deep = new Message("leaf");
        for (var i = 0; i < PlcEngineeringMessageProjector.MaxDepth + 2; i++) deep = new Message("level" + i, deep);
        var (_, deepComplete, deepOmission) = ProjectMessages(new[] { deep });
        Assert.False(deepComplete);
        Assert.Contains("deeper", deepOmission);
    }
}
