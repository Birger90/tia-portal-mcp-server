using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// External sources, project-tree folders, file exports and the accessible-device scan end to end over MCP
/// against the stateful <c>source-folders</c> FakeWorker fixture (payload shapes only; no Siemens evidence).
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcSourceFolderMcpTests
{
    private const string Scenario = "source-folders";

    private static async Task<JsonElement> CallAsync(McpProtocolTestHarness harness, string tool, object[] operations, bool? dryRun = null)
    {
        var arguments = new Dictionary<string, object?> { ["operations"] = operations };
        if (dryRun is not null) arguments["dryRun"] = dryRun;
        var reply = await harness.Client.CallToolAsync(tool, arguments);
        var root = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);
        return root;
    }

    private static JsonElement Item(JsonElement root, int index) => root.GetProperty("batch").GetProperty("operations")[index];

    private static JsonElement Result(JsonElement root, int index = 0)
    {
        var item = Item(root, index);
        Assert.True(item.GetProperty("status").GetString() == "succeeded", item.GetRawText());
        return item.GetProperty("result");
    }

    private static string[] GuardIds(JsonElement root) => root.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()!).ToArray();

    private static Task<McpProtocolTestHarness> StartAsync(TempAuditDirectory audit, McpAccessMode mode = McpAccessMode.ReadWrite)
        => McpProtocolTestHarness.StartProductionSurfaceAsync(mode, audit.Path, Scenario);

    private static string TempDirectory() => Directory.CreateTempSubdirectory("plc-source-folders-").FullName;

    [Fact]
    public async Task PlcRead_ListsSourcesFindsObjectsAndScansInReadOnlyMode()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit, McpAccessMode.ReadOnly);
        var root = await CallAsync(harness, "plc_read", new object[]
        {
            new { operationId = "sources", operation = "list_external_sources" },
            new { operationId = "find", operation = "find_objects", pattern = "*line*" },
            new { operationId = "page", operation = "find_objects", pattern = "*", kinds = new[] { "block", "block_group" }, limit = 3, offset = 2 },
            new { operationId = "scan", operation = "scan_accessible_devices", mode = "PN/IE", pcInterface = new { name = "Intel(R) Ethernet", number = 1 }, maxElements = 1 },
            new { operationId = "wrong", operation = "scan_accessible_devices", mode = "PN/IE", pcInterface = new { name = "Other", number = 1 } },
        });

        var sources = Result(root, 0).GetProperty("plcs")[0].GetProperty("sources");
        Assert.Equal(new[] { "Main_src.scl", "UnitSource.scl" }, sources.EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal("Unit_A", sources[1].GetProperty("unitName").GetString());
        Assert.Equal(JsonValueKind.Null, sources[0].GetProperty("unitName").ValueKind);

        var found = Result(root, 1);
        var matches = found.GetProperty("matches").EnumerateArray().Select(m => (m.GetProperty("kind").GetString(), m.GetProperty("name").GetString(), m.GetProperty("path").GetString())).ToArray();
        Assert.Equal(new (string?, string?, string?)[]
        {
            ("block", "FB_Line", "PLC_1/Blocks/FB_Line"),
            ("block_group", "Line", "PLC_1/Blocks/Line"),
            ("tag_table_group", "Line", null),
        }, matches);
        Assert.True(found.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, found.GetProperty("nextOffset").ValueKind);

        var page = Result(root, 2);
        Assert.Equal(3, page.GetProperty("matches").GetArrayLength());
        Assert.Equal(5, page.GetProperty("nextOffset").GetInt32());
        Assert.Equal(10, page.GetProperty("totalMatches").GetInt32());
        Assert.Contains(page.GetProperty("matches").EnumerateArray(), m => m.GetProperty("isSystem").GetBoolean() == false);

        var scan = Result(root, 3);
        Assert.Equal(2, scan.GetProperty("deviceCount").GetInt32());
        Assert.True(scan.GetProperty("truncated").GetBoolean());
        Assert.Equal("28-63-36-00-00-01", scan.GetProperty("devices")[0].GetProperty("macAddress").GetString());
        Assert.Equal("target_not_found", Item(root, 4).GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task PlcRead_ScanNeedsAnExplicitInterface()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit, McpAccessMode.ReadOnly);
        var root = await CallAsync(harness, "plc_read", new object[] { new { operationId = "scan", operation = "scan_accessible_devices", mode = "PN/IE" } });
        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("no default PC interface", root.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task PlcWrite_ImportsGeneratesAndDeletesAnExternalSourceInOneCall()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var directory = TempDirectory();
        try
        {
            var file = Path.Combine(directory, "Pump.scl");
            File.WriteAllText(file, "FUNCTION_BLOCK \"Pump\"\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\n");
            var operations = new object[]
            {
                new { operationId = "import", operation = "import_external_source", filePath = file },
                new { operationId = "generate", operation = "generate_blocks_from_source", sourceName = "Pump.scl", targetGroupPath = "PLC_1/Blocks/Line" },
                new { operationId = "delete", operation = "delete_external_source", sourceName = "Pump.scl" },
            };
            var preview = await CallAsync(harness, "plc_write", operations, dryRun: true);
            Assert.Equal("preview", preview.GetProperty("phase").GetString());
            Assert.Equal("import", preview.GetProperty("effects")[1].GetProperty("effect").GetProperty("dependsOn").GetString());
            Assert.Contains("plc_generates_from_source", GuardIds(preview));
            Assert.Contains("plc_deletes_external_source", GuardIds(preview));

            var applied = await CallAsync(harness, "plc_write", operations);
            Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
            Assert.Equal("Pump.scl", Result(applied, 0).GetProperty("sourceName").GetString());
            var generated = Result(applied, 1);
            Assert.Equal("PLC_1/Blocks/Line", generated.GetProperty("targetGroupPath").GetString());
            Assert.Equal("Pump", generated.GetProperty("generated")[0].GetProperty("name").GetString());
            Assert.True(Result(applied, 2).GetProperty("readBackConfirmed").GetBoolean());

            var read = await CallAsync(harness, "plc_read", new object[]
            {
                new { operationId = "sources", operation = "list_external_sources" },
                new { operationId = "pump", operation = "find_objects", pattern = "Pump", kinds = new[] { "block" } },
            });
            Assert.DoesNotContain(Result(read, 0).GetProperty("plcs")[0].GetProperty("sources").EnumerateArray(), s => s.GetProperty("name").GetString() == "Pump.scl");
            Assert.Equal("PLC_1/Blocks/Line/Pump", Result(read, 1).GetProperty("matches")[0].GetProperty("path").GetString());

            var missing = await CallAsync(harness, "plc_write", new object[] { new { operationId = "gone", operation = "delete_external_source", sourceName = "Pump.scl" } });
            Assert.Equal("target_not_found", missing.GetProperty("error").GetProperty("category").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlcWrite_CreatesAndDeletesTypeAndTagTableGroupsWithDependsOnAndContentGuards()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var operations = new object[]
        {
            new { operationId = "type", operation = "create_type_group", typePath = "PLC_1/Types/Motion/Drives" },
            new { operationId = "nested", operation = "create_type_group", typePath = "PLC_1/Types/Motion/Drives/Servo" },
            new { operationId = "unitType", operation = "create_type_group", typePath = "PLC_1/Units/Unit_A/Types/Local" },
            new { operationId = "tag", operation = "create_tag_table_group", folderPath = "/Line/Spare" },
            new { operationId = "unitTag", operation = "create_tag_table_group", folderPath = "/UnitTags/Sub", unitName = "Unit_A" },
            new { operationId = "table", operation = "create_tag_table", tableName = "Spare_1", folderPath = "/Line/Spare" },
            new { operationId = "dropType", operation = "delete_type_group", typePath = "PLC_1/Types/Motion" },
            new { operationId = "dropTag", operation = "delete_tag_table_group", folderPath = "/Line" },
            new { operationId = "dropUnitTag", operation = "delete_tag_table_group", folderPath = "/UnitTags/Sub", unitName = "Unit_A" },
        };
        var preview = await CallAsync(harness, "plc_write", operations, dryRun: true);
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        var effects = preview.GetProperty("effects");
        string? DependsOn(int index) => effects[index].GetProperty("effect").GetProperty("dependsOn").GetString();
        Assert.Equal("type", DependsOn(1));
        Assert.Equal("tag", DependsOn(5));
        // Like delete_block_group, a group delete depends on items that touched the group or its ancestors only.
        Assert.Null(DependsOn(6));
        Assert.Null(DependsOn(7));
        Assert.Equal("unitTag", DependsOn(8));
        var guards = preview.GetProperty("guards").EnumerateArray().ToList();
        var typeGuard = guards.Single(g => g.GetProperty("id").GetString() == "plc_deletes_group_contents" && g.GetProperty("operationId").GetString() == "dropType");
        Assert.Equal("info", typeGuard.GetProperty("severity").GetString());
        Assert.Contains("1 PLC data types (PLC_1/Types/Motion/UDT_B) and 2 groups", typeGuard.GetProperty("message").GetString());
        var tagGuard = guards.Single(g => g.GetProperty("id").GetString() == "plc_deletes_group_contents" && g.GetProperty("operationId").GetString() == "dropTag");
        Assert.Contains("removes 2 tag tables (/Line/Motors, /Line/Spare/Spare_1) with 1 tags and 0 user constants and 1 folders", tagGuard.GetProperty("message").GetString());
        Assert.DoesNotContain(guards, g => g.GetProperty("operationId").GetString() == "dropUnitTag");
        Assert.Equal(new[] { "PLC_1/Types/Motion/Drives", "PLC_1/Types/Motion/Drives/Servo" },
            effects[6].GetProperty("effect").GetProperty("removes").GetProperty("groups").EnumerateArray().Select(g => g.GetString()));

        // The fixture scripts no create_tag_table; the applied call leaves that item out.
        var root = await CallAsync(harness, "plc_write", operations.Where((_, index) => index != 5).ToArray());
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("TypeGroup", Result(root, 0).GetProperty("kind").GetString());
        Assert.Equal("Drives", Result(root, 0).GetProperty("name").GetString());
        Assert.Equal("PLC_1/Units/Unit_A/Types/Local", Result(root, 2).GetProperty("path").GetString());
        Assert.Equal("Unit_A", Result(root, 4).GetProperty("unitName").GetString());
        Assert.Equal("/Line", Result(root, 6).GetProperty("path").GetString());
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean(), root.GetRawText());

        var after = await CallAsync(harness, "plc_read", new object[]
            { new { operationId = "find", operation = "find_objects", pattern = "*", kinds = new[] { "type_group", "tag_table_group" } } });
        Assert.Equal(new[] { "PLC_1/Units/Unit_A/Types/Local", "/UnitTags" },
            Result(after).GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("kind").GetString() == "type_group" ? m.GetProperty("path").GetString() : m.GetProperty("folderPath").GetString()));

        var root2 = await CallAsync(harness, "plc_write", new object[] { new { operationId = "x", operation = "delete_type_group", typePath = "PLC_1/Types" } });
        Assert.Equal("validation_error", root2.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("system group", root2.GetProperty("error").GetProperty("message").GetString());
        var gone = await CallAsync(harness, "plc_write", new object[] { new { operationId = "x", operation = "delete_tag_table_group", folderPath = "/Line" } });
        Assert.Equal("target_not_found", gone.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task PlcWrite_CreatesRenamesAndDeletesSoftwareUnits()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "plc_write", new object[]
        {
            new { operationId = "create", operation = "create_software_unit", unitName = "Unit_B" },
            new { operationId = "group", operation = "create_block_group", blockPath = "PLC_1/Units/Unit_B/Blocks/Area" },
            new { operationId = "rename", operation = "rename_software_unit", unitName = "Unit_A", newName = "Unit_C" },
            new { operationId = "source", operation = "delete_external_source", sourceName = "UnitSource.scl", unitName = "Unit_C" },
            new { operationId = "delete", operation = "delete_software_unit", unitName = "Unit_C" },
        }, dryRun: true);
        Assert.Equal("preview", root.GetProperty("phase").GetString());
        var effects = root.GetProperty("effects");
        Assert.Equal("create", effects[1].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.Equal("rename", effects[3].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.Equal("rename", effects[4].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.Equal(new[] { "PLC_1/Units/Unit_C/Blocks/Unit_FC" },
            effects[4].GetProperty("effect").GetProperty("removes").GetProperty("blocks").EnumerateArray().Select(b => b.GetString()));
        var deleteGuard = root.GetProperty("guards").EnumerateArray()
            .Single(g => g.GetProperty("id").GetString() == "plc_deletes_group_contents" && g.GetProperty("operationId").GetString() == "delete");
        Assert.Contains("1 tag table folders and 0 external sources", deleteGuard.GetProperty("message").GetString());
        Assert.Contains("plc_renames_group", GuardIds(root));

        var applied = await CallAsync(harness, "plc_write", new object[]
        {
            new { operationId = "create", operation = "create_software_unit", unitName = "Unit_B" },
            new { operationId = "rename", operation = "rename_software_unit", unitName = "Unit_A", newName = "Unit_C" },
        });
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal("SoftwareUnit", Result(applied, 0).GetProperty("kind").GetString());
        Assert.Equal("PLC_1/Units/Unit_C", Result(applied, 1).GetProperty("newPath").GetString());
        var sources = await CallAsync(harness, "plc_read", new object[] { new { operationId = "s", operation = "list_external_sources" } });
        Assert.Contains(Result(sources).GetProperty("plcs")[0].GetProperty("sources").EnumerateArray(), s => s.GetProperty("unitName").GetString() == "Unit_C");

        var deleted = await CallAsync(harness, "plc_write", new object[] { new { operationId = "delete", operation = "delete_software_unit", unitName = "Unit_C" } });
        Assert.True(deleted.GetProperty("success").GetBoolean(), deleted.GetRawText());
        Assert.Equal("Unit_C", Result(deleted).GetProperty("name").GetString());
        var missing = await CallAsync(harness, "plc_write", new object[] { new { operationId = "x", operation = "rename_software_unit", unitName = "Unit_C", newName = "Y" } });
        Assert.Equal("target_not_found", missing.GetProperty("error").GetProperty("category").GetString());
        var bad = await CallAsync(harness, "plc_write", new object[] { new { operationId = "x", operation = "create_software_unit", unitName = "A/B" } });
        Assert.Equal("validation_error", bad.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task PlcRead_ReadsTagCommentsPerLanguage()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit, McpAccessMode.ReadOnly);
        var root = await CallAsync(harness, "plc_read", new object[]
        {
            new { operationId = "all", operation = "read_tag_comments" },
            new { operationId = "one", operation = "read_tag_comments", tableName = "Motors", folderPath = "/Line" },
        });
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        var all = Result(root, 0);
        Assert.True(all.GetProperty("complete").GetBoolean());
        var start = all.GetProperty("plcs")[0].GetProperty("tables")[0].GetProperty("members")[0];
        Assert.Equal(("Tag", "Start"), (start.GetProperty("kind").GetString(), start.GetProperty("name").GetString()));
        Assert.Equal(new[] { "de-DE:Starttaste", "en-US:Start button" },
            start.GetProperty("comment").EnumerateArray().Select(c => $"{c.GetProperty("culture").GetString()}:{c.GetProperty("text").GetString()}"));
        var one = Assert.Single(Result(root, 1).GetProperty("plcs")[0].GetProperty("tables").EnumerateArray());
        Assert.Equal("Motors", one.GetProperty("tableName").GetString());
    }

    [Fact]
    public async Task PlcWrite_RenamesGroupsAndLaterItemsSeeTheNewPaths()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "plc_write", new object[]
        {
            new { operationId = "blocks", operation = "rename_block_group", blockPath = "PLC_1/Blocks/Line", newName = "Line2" },
            new { operationId = "types", operation = "rename_type_group", typePath = "PLC_1/Types/Motion", newName = "Axes" },
            new { operationId = "tags", operation = "rename_tag_table_group", folderPath = "/Line", newName = "Area" },
            new { operationId = "watch", operation = "rename_watch_table_group", folderPath = "/Ops", newName = "Service" },
            new { operationId = "again", operation = "rename_block_group", blockPath = "PLC_1/Blocks/Line2/Cell", newName = "Cell2" },
        });
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("PLC_1/Blocks/Line2", Result(root, 0).GetProperty("newPath").GetString());
        Assert.Equal("PLC_1/Types/Axes", Result(root, 1).GetProperty("newPath").GetString());
        Assert.Equal("/Area", Result(root, 2).GetProperty("newPath").GetString());
        Assert.Equal("/Service", Result(root, 3).GetProperty("newPath").GetString());
        Assert.Equal("blocks", root.GetProperty("effects")[4].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.Contains("plc_renames_group", GuardIds(root));
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean(), root.GetRawText());

        var after = await CallAsync(harness, "plc_read", new object[] { new { operationId = "find", operation = "find_objects", pattern = "*2", kinds = new[] { "block_group" } } });
        Assert.Equal(new[] { "PLC_1/Blocks/Line2", "PLC_1/Blocks/Line2/Cell2" },
            Result(after).GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("path").GetString()));

        var unknown = await CallAsync(harness, "plc_write", new object[] { new { operationId = "x", operation = "rename_type_group", typePath = "PLC_1/Types/Nope", newName = "Y" } });
        Assert.Equal("target_not_found", unknown.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task PlcWrite_ExportsTagWatchAndForceTablesAsNeutralizedCsv()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var directory = TempDirectory();
        try
        {
            var tags = Path.Combine(directory, "tags.csv");
            var watch = Path.Combine(directory, "watch.csv");
            var force = Path.Combine(directory, "force.csv");
            var root = await CallAsync(harness, "plc_write", new object[]
            {
                new { operationId = "tags", operation = "export_tag_tables_csv", filePath = tags },
                new { operationId = "watch", operation = "export_watch_tables_csv", filePath = watch, csvDelimiter = ";" },
                new { operationId = "force", operation = "export_force_tables_csv", filePath = force, rawCells = true },
            });
            Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());

            var tagResult = Result(root, 0);
            var tagBytes = File.ReadAllBytes(tags);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(tagBytes)).ToLowerInvariant(), tagResult.GetProperty("sha256").GetString());
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, tagBytes.Take(3));
            var tagText = Encoding.UTF8.GetString(tagBytes, 3, tagBytes.Length - 3);
            Assert.StartsWith("RowType,Plc,Device,Folder,Table,TableIsDefault,Name,DataType,LogicalAddress,Value,ExternalAccessible,ExternalVisible,ExternalWritable,Comment[de-DE],Comment[en-US]\r\n", tagText);
            Assert.Contains("tag,PLC_1,Station_1,/,Default tag table,true,Start,Bool,%I0.0,,true,true,false,Starttaste,Start button\r\n", tagText);
            Assert.Contains("constant,PLC_1,Station_1,/,Default tag table,true,Offset,Int,,'-5,,,,,Offset value\r\n", tagText);
            Assert.Contains("\"Motor, \"\"main\"\"\"", tagText);
            Assert.Contains(",,'=SUM(A1)\r\n", tagText);
            Assert.Equal(2, tagResult.GetProperty("neutralizedCells").GetInt32());
            Assert.Equal(2, tagResult.GetProperty("tableCount").GetInt32());
            Assert.Equal(3, tagResult.GetProperty("rowCount").GetInt32());

            var watchText = File.ReadAllText(watch);
            Assert.Contains("PLC_1;Station_1;/;Watch_1;true;0;\"\"\"Start\"\"\";%I0.0;Bool;Permanent;false;Permanent;TRUE;\r\n", watchText);
            Assert.Contains(";'=cmd;", watchText);
            Assert.Contains(";Address\r\n", watchText);
            Assert.Equal(2, Result(root, 1).GetProperty("tableCount").GetInt32());
            Assert.Contains("PLC_1,Station_1,/,Force_1,true,0,\"\"\"Valve\"\"\",%Q0.1,Bool,Permanent,true,TRUE,\r\n", File.ReadAllText(force));
            Assert.True(Result(root, 2).GetProperty("rawCells").GetBoolean());

            var refused = await CallAsync(harness, "plc_write", new object[] { new { operationId = "again", operation = "export_tag_tables_csv", filePath = tags } });
            Assert.Equal("validation_error", refused.GetProperty("error").GetProperty("category").GetString());
            Assert.Contains("overwrite:true", refused.GetProperty("error").GetProperty("message").GetString());

            var one = await CallAsync(harness, "plc_write", new object[]
                { new { operationId = "one", operation = "export_watch_tables_csv", filePath = watch, overwrite = true, tableName = "Watch_2", folderPath = "/Ops" } });
            Assert.True(one.GetProperty("success").GetBoolean(), one.GetRawText());
            Assert.Contains("plc_export_overwrites_files", GuardIds(one));
            Assert.True(Result(one).GetProperty("overwritten").GetBoolean());
            Assert.Equal(1, Result(one).GetProperty("tableCount").GetInt32());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlcWrite_ExportsBlocksAndSourcesToFiles()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var directory = TempDirectory();
        try
        {
            var root = await CallAsync(harness, "plc_write", new object[] { new { operationId = "all", operation = "export_blocks", directoryPath = directory } });
            Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
            var result = Result(root);
            var files = result.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("relativePath").GetString()!).ToArray();
            Assert.Contains("Blocks/Main.xml", files);
            Assert.Contains("Blocks/Main.s7res", files);
            Assert.Contains("Blocks/Line/FC_Ladder.xml", files);
            Assert.Contains("Types/Motion/UDT_B.xml", files);
            Assert.Contains("Units/Unit_A/Blocks/Unit_FC.xml", files);
            Assert.DoesNotContain(files, f => f.Contains("Sys_FB"));
            var skipped = Assert.Single(result.GetProperty("skipped").EnumerateArray());
            Assert.Equal("PLC_1/Blocks/Line/Cell/FB_Cell", skipped.GetProperty("objectPath").GetString());
            Assert.Equal("worker_operation_failed", skipped.GetProperty("failureCategory").GetString());
            Assert.False(result.GetProperty("complete").GetBoolean());
            foreach (var file in result.GetProperty("files").EnumerateArray())
            {
                var bytes = File.ReadAllBytes(Path.Combine(directory, file.GetProperty("relativePath").GetString()!.Replace('/', Path.DirectorySeparatorChar)));
                Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), file.GetProperty("sha256").GetString());
            }

            // The manifest lists every written file with its hash; the result lists a bounded sample.
            var manifestPath = result.GetProperty("manifestPath").GetString()!;
            Assert.Equal(Path.Combine(directory, "tia-mcp-export-manifest.json"), manifestPath);
            var manifestBytes = File.ReadAllBytes(manifestPath);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(), result.GetProperty("manifestSha256").GetString());
            using (var manifest = JsonDocument.Parse(Encoding.UTF8.GetString(manifestBytes).TrimStart('\uFEFF')))
                Assert.Equal(result.GetProperty("fileCount").GetInt32(), manifest.RootElement.GetProperty("files").GetArrayLength());
            Assert.False(result.GetProperty("filesTruncated").GetBoolean());
            Assert.Equal(1, result.GetProperty("skippedCount").GetInt32());

            var refused = await CallAsync(harness, "plc_write", new object[] { new { operationId = "again", operation = "export_blocks", directoryPath = directory } });
            Assert.Equal("validation_error", refused.GetProperty("error").GetProperty("category").GetString());

            var sourceDirectory = Path.Combine(directory, "src");
            Directory.CreateDirectory(sourceDirectory);
            var source = await CallAsync(harness, "plc_write", new object[]
            {
                new { operationId = "src", operation = "export_blocks", directoryPath = sourceDirectory, blockPath = "PLC_1/Blocks", format = "source" },
                new { operationId = "file", operation = "export_source_file", filePath = Path.Combine(sourceDirectory, "Line.scl"), blockPaths = new[] { "PLC_1/FB_Line" }, typePaths = new[] { "PLC_1/Types/UDT_A" } },
            });
            Assert.True(source.GetProperty("success").GetBoolean(), source.GetRawText());
            var sourceFiles = Result(source, 0).GetProperty("files").EnumerateArray().Select(f => f.GetProperty("relativePath").GetString()).ToArray();
            Assert.Contains("Blocks/FB_Line.scl", sourceFiles);
            Assert.Contains("Blocks/Settings_DB.db", sourceFiles);
            Assert.Contains(Result(source, 0).GetProperty("skipped").EnumerateArray(), s => s.GetProperty("objectPath").GetString() == "PLC_1/Blocks/Main"
                && s.GetProperty("failureCategory").ValueKind == JsonValueKind.Null);
            Assert.Equal(new[] { "PLC_1/FB_Line", "PLC_1/Types/UDT_A" }, Result(source, 1).GetProperty("objects").EnumerateArray().Select(o => o.GetString()));
            Assert.True(File.Exists(Path.Combine(sourceDirectory, "Line.scl")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlcWrite_UnitDeleteListsTheUnitsExternalSources()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var preview = await CallAsync(harness, "plc_write", new object[] { new { operationId = "delete", operation = "delete_software_unit", unitName = "Unit_A" } }, dryRun: true);
        var guard = preview.GetProperty("guards").EnumerateArray().Single(g => g.GetProperty("id").GetString() == "plc_deletes_group_contents");
        Assert.Contains("and 1 external sources", guard.GetProperty("message").GetString());
        Assert.Contains("alarm text lists", guard.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PlcWrite_AnUnreadableSourceInventoryInformsInsteadOfFailingThePlan()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "source-folders-unreadable-sources");
        // Previewed first: the delete below runs and removes Unit_A.
        var rename = await CallAsync(harness, "plc_write", new object[] { new { operationId = "rename", operation = "rename_software_unit", unitName = "Unit_A", newName = "Unit_B" } }, dryRun: true);
        Assert.Equal("preview", rename.GetProperty("phase").GetString());
        Assert.Contains("is incomplete", rename.GetProperty("guards").EnumerateArray().Single(g => g.GetProperty("id").GetString() == "plc_renames_group").GetProperty("message").GetString());

        // The guard informs (owner decision: never stricter than TIA Portal); the delete runs and TIA Portal decides.
        var unit = await CallAsync(harness, "plc_write", new object[] { new { operationId = "delete", operation = "delete_software_unit", unitName = "Unit_A" } });
        Assert.Equal("applied", unit.GetProperty("phase").GetString());
        var guards = unit.GetProperty("guards").EnumerateArray().ToList();
        Assert.Contains(guards, g => g.GetProperty("id").GetString() == "plc_state_unverifiable"
            && g.GetProperty("message").GetString()!.Contains("external source inventory of PLC 'PLC_1' is incomplete (The external sources of PLC 'PLC_1' could not be read"));
        Assert.Contains(guards, g => g.GetProperty("id").GetString() == "plc_deletes_group_contents");

        var source = await CallAsync(harness, "plc_write", new object[] { new { operationId = "src", operation = "delete_external_source", sourceName = "Main_src.scl" } });
        Assert.NotEqual("blocked", source.GetProperty("phase").GetString());
        Assert.Contains(source.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == "plc_state_unverifiable");
    }

    [Fact]
    public async Task PlcWrite_WatchCsvOfAMissingGroupIsNotFound()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var directory = TempDirectory();
        try
        {
            var root = await CallAsync(harness, "plc_write", new object[]
                { new { operationId = "watch", operation = "export_watch_tables_csv", filePath = Path.Combine(directory, "w.csv"), folderPath = "/Nope" } });
            var item = Item(root, 0);
            Assert.Equal("failed", item.GetProperty("status").GetString());
            Assert.Equal("target_not_found", item.GetProperty("failure").GetProperty("category").GetString());
            Assert.False(File.Exists(Path.Combine(directory, "w.csv")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlcRead_FindsSoftwareUnitsTagsAndUserConstants()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit, McpAccessMode.ReadOnly);
        var root = await CallAsync(harness, "plc_read", new object[]
        {
            new { operationId = "units", operation = "find_objects", pattern = "Unit_*", kinds = new[] { "software_unit" } },
            new { operationId = "tags", operation = "find_objects", pattern = "*", kinds = new[] { "tag", "user_constant" } },
        });
        var unit = Assert.Single(Result(root, 0).GetProperty("matches").EnumerateArray());
        Assert.Equal(("software_unit", "PLC_1/Units/Unit_A"), (unit.GetProperty("kind").GetString(), unit.GetProperty("path").GetString()));
        var tags = Result(root, 1).GetProperty("matches").EnumerateArray().Select(m =>
            $"{m.GetProperty("kind").GetString()}:{m.GetProperty("folderPath").GetString()}:{m.GetProperty("tableName").GetString()}:{m.GetProperty("name").GetString()}").ToArray();
        Assert.Equal(new[] { "tag:/:Default tag table:Start", "tag:/Line:Motors:Motor, \"main\"", "user_constant:/:Default tag table:Offset" }, tags);
    }

    [Fact]
    public async Task PlcWrite_RefusesDevicePathsAndIsDeniedInReadOnlyMode()
    {
        using var audit = new TempAuditDirectory();
        await using (var harness = await StartAsync(audit))
        {
            // UNC shares are allowed (owner decision 2026-11-01); device paths stay refused.
            var device = await CallAsync(harness, "plc_write", new object[] { new { operationId = "dev", operation = "export_tag_tables_csv", filePath = @"\\?\C:\tags.csv" } });
            Assert.Equal("validation_error", device.GetProperty("error").GetProperty("category").GetString());
            Assert.Contains("normalization", device.GetProperty("error").GetProperty("message").GetString());
        }

        await using var readOnly = await StartAsync(audit, McpAccessMode.ReadOnly);
        var tools = await readOnly.Client.ListToolsAsync();
        Assert.DoesNotContain(tools, tool => tool.Name == "plc_write");
    }
}
