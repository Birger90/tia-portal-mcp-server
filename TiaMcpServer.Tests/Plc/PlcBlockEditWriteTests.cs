using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// Granular block reads and writes through plc_read's projection and plc_write's guarded pipeline,
/// against the stateful plc-write-roundtrip FakeWorker (its PLC_1 carries the global DB Settings,
/// FB Motors/FB_Motor with instance DB FB_Motor_DB, the PLC data type UDT_Settings, the fail-safe
/// Safety_FB and the know-how-protected Protected_FC).
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcBlockEditWriteTests
{
    private const string Roundtrip = "plc-write-roundtrip";

    private static PlcOperationRequest Op(string id, string operation, Action<PlcOperationRequest>? configure = null)
    {
        var request = new PlcOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    private static async Task<JsonElement> ReadAsync(PlcGuardedWriteFixture fixture, PlcOperationRequest read)
    {
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { read }).IsValid, PlcOperationCatalog.ValidateRead(new[] { read }).Error);
        var item = PlcPayloadContract.Project(read, await PlcWorkerInvoker.InvokeReadAsync(fixture.Client, read));
        Assert.True(item.Result is not null, item.Failure?.Message);
        return item.Result!.Value;
    }

    private static async Task<PlcBlockInterfaceInfo> InterfaceAsync(PlcGuardedWriteFixture fixture, string blockPath)
        => CanonicalJson.Deserialize<PlcBlockInterfaceInfo>((await ReadAsync(fixture, Op("r", "get_block_interface", r => r.BlockPath = blockPath))).GetRawText());

    [Fact]
    public async Task BlockInterfaceRead_IsTypedAndCarriesTheContentHash()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var info = await InterfaceAsync(fixture, "Station_1/Settings");

        Assert.Equal(("PLC_1/Blocks/Settings", "GlobalDB", 3), (info.Block.BlockPath, info.Block.BlockType, info.Block.Number));
        Assert.Equal("SW.Blocks.GlobalDB", info.DocumentElement);
        Assert.Equal(await fixture.ReadHashAsync("Station_1/Settings", "xml"), info.ContentHash);
        var members = Assert.Single(info.Sections).Members;
        Assert.Equal(new[] { "Speed", "Limits", "Config", "Buffer" }, members.Select(m => m.Name));
        Assert.Equal("Set speed", members[0].Comments.Single().Text);
        Assert.Equal("\"UDT_Settings\"", members[2].DataType);
    }

    [Fact]
    public async Task TypeInterfaceRead_ListsTheSectionNone()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var info = CanonicalJson.Deserialize<PlcTypeInterfaceInfo>((await ReadAsync(fixture, Op("t", "get_type_interface", r => r.TypePath = "Station_1/UDT_Settings"))).GetRawText());

        Assert.Equal("None", Assert.Single(info.Sections).Name);
        Assert.Equal("Scaling.Type", info.Sections[0].Members[0].Members[0].Path);
        Assert.StartsWith("xml:sha256:", info.ContentHash);
    }

    [Fact]
    public async Task DbValues_ExpandGlobalAndInstanceDbs()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var global = CanonicalJson.Deserialize<PlcDbValuesInfo>((await ReadAsync(fixture, Op("g", "get_db_values", r => r.BlockPath = "Station_1/Settings"))).GetRawText());
        var instance = CanonicalJson.Deserialize<PlcDbValuesInfo>((await ReadAsync(fixture, Op("i", "get_db_values", r =>
        {
            r.BlockPath = "Station_1/FB_Motor_DB";
            r.WithAttributes = true;
        }))).GetRawText());
        var narrowed = CanonicalJson.Deserialize<PlcDbValuesInfo>((await ReadAsync(fixture, Op("n", "get_db_values", r =>
        {
            r.BlockPath = "Station_1/Settings";
            r.MemberPath = "Buffer[2]";
        }))).GetRawText());

        Assert.True(global.IsComplete);
        Assert.Equal(new[] { "Speed", "Limits.regMin", "Limits.regMax", "Config.Scaling.Type", "Config.Scaling.valueMin", "Buffer[0]", "Buffer[1]", "Buffer[2]", "Buffer[3]" },
            global.Values.Select(v => v.Path));
        Assert.Equal(("100", "-500", "TRUE"), (global.Values[0].StartValue, global.Values[1].StartValue, global.Values[3].StartValue));
        Assert.Equal("FB_Motor", instance.Block.InstanceOfName);
        Assert.Equal(new[] { "Enable", "Count" }, instance.Values.Select(v => v.Path));
        Assert.True(instance.Values[1].Attributes!.ExternalAccessible);
        Assert.Equal("Buffer[2]", Assert.Single(narrowed.Values).Path);
    }

    [Fact]
    public async Task InterfaceEditPreview_ShowsChangesDiffAndGuardWithoutWriting()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var before = await InterfaceAsync(fixture, "Station_1/Settings");

        var response = await fixture.RunAsync(true, Op("add", "add_interface_member", r =>
        {
            r.BlockPath = "Station_1/Settings";
            r.Section = "Static";
            r.MemberPath = "Limits.regHyst";
            r.DataType = "Int";
            r.StartValue = "5";
        }));

        Assert.Equal("preview", response.Phase);
        Assert.True(response.Success, response.Error?.Message);
        var effect = Assert.Single(response.Effects).Effect!;
        Assert.Equal("PLC_1/Blocks/Settings", effect.Target.BlockPath);
        Assert.Equal(new[] { ("member", (string?)null, (string?)"Static.Limits.regHyst"), ("dataType", null, "Int"), ("startValue", null, "5") },
            effect.Changes.Select(c => (c.Field, c.Current, c.Requested)));
        Assert.NotNull(effect.ContentDiff);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.ChangesInterface && g.Severity == "info");
        Assert.Equal(before.ContentHash, (await InterfaceAsync(fixture, "Station_1/Settings")).ContentHash);
    }

    [Fact]
    public async Task InterfaceEdits_ApplyThroughUpdateBlockLogicAndVerifyByReRead()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var hash = (await InterfaceAsync(fixture, "Station_1/Settings")).ContentHash;

        var response = await fixture.RunAsync(false,
            Op("start", "update_interface_member", r =>
            {
                r.BlockPath = "Station_1/Settings";
                r.Section = "Static";
                r.MemberPath = "Speed";
                r.StartValue = "250";
                r.ExpectedContentHash = hash;
            }),
            Op("add", "add_interface_member", r =>
            {
                r.BlockPath = "Station_1/Blocks/Settings";
                r.Section = "Static";
                r.MemberPath = "Ramp";
                r.DataType = "Time";
                r.Comment = "Ramp time";
                r.CommentCulture = "en-US";
                r.InsertBefore = "Limits";
            }),
            Op("drop", "delete_interface_member", r =>
            {
                r.BlockPath = "Station_1/Settings";
                r.Section = "Static";
                r.MemberPath = "Buffer";
            }));

        Assert.True(response.Success, response.Error?.Message ?? string.Join("; ", response.Batch!.Operations.Select(o => o.Failure?.Message)));
        Assert.Equal(new[] { null, "start", "add" }, response.Effects.Select(e => e.Effect!.DependsOn));
        // Edits of different members of one block supersede per member (K03): each is checked by the final re-read.
        Assert.Equal(new[] { "interfaceMember", "interfaceMember", "interfaceMember" }, response.Verification!.Operations.Select(v => v.Check));
        Assert.All(response.Verification.Operations, v => Assert.True(v.Success, v.Message));
        Assert.Equal("Static.Buffer absent", response.Verification.Operations[^1].Actual);
        var after = await InterfaceAsync(fixture, "Station_1/Settings");
        Assert.All(response.Verification.Operations, v => Assert.Equal(after.ContentHash, v.ContentHash));
        var members = after.Sections[0].Members;
        Assert.Equal(new[] { "Speed", "Ramp", "Limits", "Config" }, members.Select(m => m.Name));
        Assert.Equal("250", members[0].StartValue);
        Assert.Equal("Ramp time", members[1].Comments.Single().Text);
        Assert.Equal(3, requests.Methods().Count(m => m == "update_block_logic"));
        var import = response.Batch!.Operations[0].Result!.Value;
        Assert.Equal("xml", import.GetProperty("format").GetString());
        Assert.Equal("Static", import.GetProperty("section").GetString());
        Assert.Equal(JsonValueKind.Null, import.GetProperty("typeImport").ValueKind);
    }

    [Fact]
    public async Task TypeInterfaceEdit_UsesUpdateTypeContent()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, Op("udt", "update_interface_member", r =>
        {
            r.TypePath = "Station_1/UDT_Settings";
            r.MemberPath = "Scaling.valueMin";
            r.StartValue = "0.5";
        }));

        Assert.True(response.Success, response.Batch?.Operations[0].Failure?.Message ?? response.Error?.Message);
        Assert.Equal("Type", response.Effects[0].Effect!.Target.Kind);
        Assert.Equal("update_type_content", response.Batch!.Operations[0].Result!.Value.GetProperty("typeImport").GetProperty("operation").GetString());
        var info = CanonicalJson.Deserialize<PlcTypeInterfaceInfo>((await ReadAsync(fixture, Op("t", "get_type_interface", r => r.TypePath = "Station_1/UDT_Settings"))).GetRawText());
        Assert.Equal("0.5", info.Sections[0].Members[0].Members[1].StartValue);
    }

    [Theory]
    [InlineData("Station_1/Protected_FC", "Temp", WorkerFailureCategories.TargetKindUnsupported)]
    [InlineData("Station_1/FB_Motor_DB", "Static", WorkerFailureCategories.TargetKindUnsupported)]
    [InlineData("Station_1/Settings", "Input", WorkerFailureCategories.TargetNotFound)]
    [InlineData("Station_1/Nope", "Static", WorkerFailureCategories.TargetNotFound)]
    public async Task InterfaceEditRefusals_StopTheCallBeforeAnyImport(string blockPath, string section, string category)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, Op("x", "add_interface_member", r =>
        {
            r.BlockPath = blockPath;
            r.Section = section;
            r.MemberPath = "Extra";
            r.DataType = "Int";
        }));

        Assert.Equal("error", response.Phase);
        Assert.Equal(category, response.Error!.Category);
        Assert.DoesNotContain("update_block_logic", requests.Methods());
    }

    /// <summary>
    /// K55 (live V21 2026-11-02): create_block, then add_interface_member on the new block in one call. TIA Portal exports no
    /// SimaticML of a block not compiled since it was created, so the edit cannot be planned; it failed with a misleading
    /// state_changed ("no planned document") and its verification said success true. Now the edit fails with the planning
    /// reason (compile_check first), nothing is sent for it, and the create keeps its own read-back (K57).
    /// </summary>
    [Fact]
    public async Task CreateBlockThenInterfaceEdit_FailsWithTheExportReasonAndNoFalseVerification()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false,
            Op("create", "create_block", r =>
            {
                r.BlockPath = "Station_1/Blocks/K55_DB";
                r.BlockType = "GlobalDB";
            }),
            Op("add", "add_interface_member", r =>
            {
                r.BlockPath = "Station_1/K55_DB";
                r.Section = "Static";
                r.MemberPath = "Speed";
                r.DataType = "Int";
            }));

        Assert.False(response.Success);
        Assert.Equal("add", response.Effects[1].OperationId);
        Assert.Equal("create", response.Effects[1].Effect!.DependsOn);
        var failure = response.Batch!.Operations[1].Failure!;
        Assert.NotEqual(WorkerFailureCategories.StateChanged, failure.Category);
        Assert.Contains("SimaticML", failure.Message);
        Assert.Contains("compile_check", failure.Message);
        Assert.DoesNotContain("update_block_logic", requests.Methods());
        // K60: nothing was sent for the edit, so its warning says only that the earlier create stays applied.
        Assert.Contains(WriteExecution.PartialWriteNotMutatedMessage, response.Batch.Operations[1].Warnings);
        Assert.DoesNotContain(WriteExecution.PartialWriteMessage, response.Batch.Operations[1].Warnings);
        // Nothing was imported for the edit: no verification entry claims anything for it.
        Assert.DoesNotContain(response.Verification!.Operations, v => v.OperationId == "add");
        var created = Assert.Single(response.Verification.Operations);
        Assert.Equal(("create", "block", true), (created.OperationId, created.Check, created.Success));
        Assert.Contains("worker re-resolved the created block", created.Message);
    }

    [Fact]
    public async Task StaleExpectedHash_IsStateChanged()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, Op("x", "delete_interface_member", r =>
        {
            r.BlockPath = "Station_1/Settings";
            r.Section = "Static";
            r.MemberPath = "Speed";
            r.ExpectedContentHash = "xml:sha256:" + new string('0', 64);
        }));

        Assert.Equal(WorkerFailureCategories.StateChanged, response.Error!.Category);
    }

    [Fact]
    public async Task DbMemberUpdate_SetsStartValuesAtAnyDepthAndVerifies()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false,
            Op("nested", "update_db_member", r =>
            {
                r.BlockPath = "Station_1/Settings";
                r.MemberPath = "Config.Scaling.Type";
                r.StartValue = "FALSE";
                r.Retain = true;
            }),
            Op("element", "update_db_member", r =>
            {
                r.BlockPath = "Station_1/Settings";
                r.MemberPath = "Buffer[1]";
                r.StartValue = "2.5";
            }),
            Op("instance", "update_db_member", r =>
            {
                r.BlockPath = "Station_1/FB_Motor_DB";
                r.MemberPath = "Count";
                r.StartValue = "7";
            }));

        Assert.True(response.Success, string.Join("; ", response.Batch!.Operations.Select(o => o.Failure?.Message)));
        Assert.Equal(new[] { ("startValue", (string?)"TRUE", (string?)"FALSE"), ("retain", "false", "true") },
            response.Effects[0].Effect!.Changes.Select(c => (c.Field, c.Current, c.Requested)));
        Assert.Equal("nested", response.Effects[1].Effect!.DependsOn);
        Assert.Equal(new[] { "dbMember", "dbMember", "dbMember" }, response.Verification!.Operations.Select(v => v.Check));
        Assert.All(response.Verification.Operations, v => Assert.True(v.Success, v.Message));
        var values = CanonicalJson.Deserialize<PlcDbValuesInfo>((await ReadAsync(fixture, Op("g", "get_db_values", r => r.BlockPath = "Station_1/Settings"))).GetRawText());
        Assert.Equal("FALSE", values.Values.Single(v => v.Path == "Config.Scaling.Type").StartValue);
        Assert.Equal("2.5", values.Values.Single(v => v.Path == "Buffer[1]").StartValue);
    }

    [Fact]
    public async Task DbMemberUpdate_IgnoredByPortalFailsAsPostcondition()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, Op("x", "update_db_member", r =>
        {
            r.BlockPath = "Station_1/Settings";
            r.MemberPath = "Speed";
            r.StartValue = "IgnoredByPortal";
        }));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, response.Batch!.Operations[0].Failure!.Category);
        // K58: the failed item's dbMember read-back reports the values read afterwards, never success true.
        var entry = Assert.Single(response.Verification!.Operations);
        Assert.Equal(("dbMember", false), (entry.Check, entry.Success));
        Assert.StartsWith("Operation 'x' failed with postcondition_failed", entry.Message);
    }

    [Fact]
    public async Task DbMemberUpdate_UnknownMemberFailsAtPlan()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, Op("x", "update_db_member", r =>
        {
            r.BlockPath = "Station_1/Settings";
            r.MemberPath = "Nope";
            r.StartValue = "1";
        }));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, response.Error!.Category);
    }

    [Fact]
    public async Task BlockOperations_CreateRenameRenumberCopyAndMove()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false,
            Op("idb", "create_instance_db", r =>
            {
                r.BlockPath = "Station_1/Blocks/Motors/Motor2_DB";
                r.InstanceOfName = "FB_Motor";
                r.Number = 20;
            }),
            Op("rename", "rename_block", r =>
            {
                r.BlockPath = "Station_1/Motor2_DB";
                r.NewName = "Pump_DB";
            }),
            Op("renumber", "renumber_block", r =>
            {
                r.BlockPath = "Station_1/Settings";
                r.Number = 30;
            }),
            Op("copy", "copy_block", r =>
            {
                r.BlockPath = "Station_1/Settings";
                r.NewName = "Settings_Copy";
                r.TargetGroupPath = "Station_1/Blocks/Motors";
            }),
            Op("move", "move_block", r =>
            {
                r.BlockPath = "Station_1/Blocks/Motors/FB_Motor";
                r.TargetGroupPath = "Station_1/Blocks";
            }));

        Assert.True(response.Success, string.Join("; ", response.Batch!.Operations.Select(o => o.Failure?.Message))
            + string.Join("; ", response.Verification?.Operations.Select(v => v.Message) ?? Array.Empty<string>()));
        Assert.Equal(new[] { "superseded", "block", "block", "block", "block" }, response.Verification!.Operations.Select(v => v.Check));
        Assert.All(response.Verification.Operations, v => Assert.True(v.Success, v.Message));
        Assert.StartsWith("xml:sha256:", response.Verification.Operations[1].ContentHash);
        Assert.Equal("idb", response.Effects[1].Effect!.DependsOn);
        var copyEffect = response.Effects[3].Effect!;
        Assert.Equal("PLC_1/Blocks/Motors", copyEffect.Placement);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.BlockRoundTrip && g.OperationId == "move");
        Assert.Equal("PLC_1/Blocks/Motors/Pump_DB", (await InterfaceAsync(fixture, "Station_1/Pump_DB")).Block.BlockPath);
        Assert.Equal(30, (await InterfaceAsync(fixture, "Station_1/Settings")).Block.Number);
        Assert.Equal("PLC_1/Blocks/Motors/Settings_Copy", (await InterfaceAsync(fixture, "Station_1/Settings_Copy")).Block.BlockPath);
        Assert.Equal("PLC_1/Blocks/FB_Motor", (await InterfaceAsync(fixture, "Station_1/FB_Motor")).Block.BlockPath);
        var moved = response.Batch.Operations[4].Result!.Value;
        Assert.Equal("PLC_1/Blocks/Motors/FB_Motor", moved.GetProperty("sourceBlockPath").GetString());
    }

    [Theory]
    [InlineData("add_interface_member")]
    [InlineData("copy_block")]
    public async Task FailSafeBlockEdits_FollowTheSafetyLoginRule_InsteadOfARefusal(string operation)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        // Safety_FB's PLC has a safety password and the session is not logged on: TIA Portal's rule blocks it before any change.
        var response = await fixture.RunAsync(false, Op("x", operation, r =>
        {
            r.BlockPath = "Station_1/Safety_FB";
            r.Section = operation == "add_interface_member" ? "Static" : null;
            r.MemberPath = operation == "add_interface_member" ? "Extra" : null;
            r.DataType = operation == "add_interface_member" ? "Int" : null;
            r.NewName = operation == "copy_block" ? "Clone" : null;
        }));

        Assert.Equal("blocked", response.Phase);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.SafetyLoginRequired);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.SafetyAcceptance);
        Assert.DoesNotContain("update_block_logic", requests.Methods());
        Assert.DoesNotContain("copy_block", requests.Methods());
    }

    [Theory]
    [InlineData("Station_1/Protected_FC")]
    public async Task CopyRefusesProtectedBlocks(string blockPath)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, Op("x", "copy_block", r =>
        {
            r.BlockPath = blockPath;
            r.NewName = "Clone";
        }));

        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, response.Batch!.Operations[0].Failure!.Category);
        // K58: the failed copy's block read-back reports where the copy would be, never success true.
        Assert.All(response.Verification!.Operations, v => Assert.False(v.Success));
        Assert.All(response.Verification.Operations, v => Assert.Contains("failed with target_kind_unsupported", v.Message));
    }

    [Fact]
    public async Task MoveTagTable_MovesTheTableAndVerifiesTheCounts()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, Op("move", "move_tag_table", r =>
        {
            r.PlcName = "Station_1";
            r.TableName = "Motors";
            r.FolderPath = "/Line";
            r.TargetFolderPath = "/";
        }));

        Assert.True(response.Success, string.Join("; ", response.Batch!.Operations.Select(o => o.Failure?.Message)) + response.Verification?.Operations[0].Message);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.MovesTagTable);
        Assert.Equal(("tagTable", true), (response.Verification!.Operations[0].Check, response.Verification.Operations[0].Success));
        var inventory = CanonicalJson.DeserializeWorkerPayload<PlcTagInventoryInfo>(await fixture.InventoryAsync());
        Assert.Equal("/", inventory.Plcs.Single(p => p.PlcName == "PLC_1").Tables.Single(t => t.Name == "Motors").FolderPath);
    }

    [Fact]
    public async Task MoveDefaultTagTable_IsRefusedByTheWorker()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, Op("move", "move_tag_table", r =>
        {
            r.PlcName = "Station_1";
            r.TableName = "Default tag table";
            r.TargetFolderPath = "/Line";
        }));

        Assert.Equal(WorkerFailureCategories.ValidationError, response.Batch!.Operations[0].Failure!.Category);
        // K58: the failed move's tagTable read-back reports the current tables, never success true.
        var entry = Assert.Single(response.Verification!.Operations);
        Assert.Equal(("tagTable", false), (entry.Check, entry.Success));
        Assert.StartsWith("Operation 'move' failed with validation_error", entry.Message);
    }

    [Fact]
    public async Task ReadOnlyMode_AllowsTheReadsAndDeniesTheWrites()
    {
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, "get_block_interface"));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, "get_db_values"));
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip, McpAccessMode.ReadOnly);

        var response = await fixture.RunAsync(false, Op("x", "rename_block", r =>
        {
            r.BlockPath = "Station_1/Settings";
            r.NewName = "Other";
        }));

        Assert.Equal(WorkerFailureCategories.AccessDenied, response.Error!.Category);
    }

    [Theory]
    [InlineData("add_interface_member", "memberPath=Speed;dataType=Int", "exactly one of blockPath")]
    [InlineData("add_interface_member", "blockPath=PLC_1/Settings;memberPath=Speed;dataType=Int", "section is required")]
    [InlineData("add_interface_member", "typePath=PLC_1/T;section=Static;memberPath=X;dataType=Int", "only the section None")]
    [InlineData("update_interface_member", "blockPath=PLC_1/Settings;section=Static;memberPath=Speed", "needs at least one")]
    [InlineData("update_interface_member", "blockPath=PLC_1/Settings;section=Static;memberPath=Speed;comment=x", "commentCulture")]
    [InlineData("delete_interface_member", "blockPath=PLC_1/Settings;section=Static;memberPath=Arr[1]", "omit the index")]
    [InlineData("update_db_member", "blockPath=PLC_1/Settings;memberPath=Speed", "needs startValue")]
    [InlineData("renumber_block", "blockPath=PLC_1/Settings", "exactly one of number")]
    [InlineData("rename_block", "blockPath=PLC_1/Settings;newName=A/B", "must not have")]
    public void ShapeErrors_AreValidationErrors(string operation, string fields, string message)
    {
        var request = new PlcOperationRequest { OperationId = "x", Operation = operation };
        foreach (var pair in fields.Split(';'))
        {
            var (name, value) = (pair[..pair.IndexOf('=')], pair[(pair.IndexOf('=') + 1)..]);
            typeof(PlcOperationRequest).GetProperty(char.ToUpperInvariant(name[0]) + name[1..])!.SetValue(request, value);
        }

        var validation = PlcOperationCatalog.ValidateWrite(new[] { request });

        Assert.False(validation.IsValid);
        Assert.Contains(message, validation.Error);
    }
}
