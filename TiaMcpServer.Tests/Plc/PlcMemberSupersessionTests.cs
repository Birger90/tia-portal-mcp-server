using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// K03 (live report 2026-11-01): two interface edits on different members of one block marked the first superseded. Member
/// edits supersede per member; whole-object items still supersede and are superseded by them.
/// </summary>
public sealed class PlcMemberSupersessionTests
{
    private const string BlockPath = "PLC_1/Blocks/DB_Settings";
    private readonly Dictionary<string, PlcWriteEffect> _effects = new(StringComparer.Ordinal);
    private int _next;

    private PlcOperationRequest Item(string operation, string? memberPath = null, Action<PlcOperationRequest>? set = null, PlcTargetIdentity? target = null)
    {
        var item = new PlcOperationRequest { OperationId = "i" + _next++, Operation = operation, BlockPath = BlockPath, MemberPath = memberPath };
        set?.Invoke(item);
        _effects[item.OperationId] = new PlcWriteEffect(operation, target ?? Block(BlockPath), Array.Empty<PlcFieldChange>(), null, null, null, null);
        return item;
    }

    private bool Supersedes(PlcOperationRequest later, PlcOperationRequest earlier)
    {
        var policy = new PlcVerificationPolicy(null!, _effects);
        var own = policy.ObjectKeys(earlier, asLater: false);
        return policy.ObjectKeys(later, asLater: true).Any(k => own.Any(o => o == k || o.StartsWith(k + "\n", StringComparison.Ordinal)));
    }

    private static PlcTargetIdentity Block(string path) => new(PlcNameRules.Block, "PLC_1", "S1", null, null, path.Split('/')[^1], path, null);

    private static PlcTargetIdentity Type(string path) => new("Type", "PLC_1", "S1", null, null, path.Split('/')[^1], null, path);

    [Theory]
    [InlineData(PlcBlockEditMethods.AddInterfaceMember)]
    [InlineData(PlcBlockEditMethods.UpdateInterfaceMember)]
    [InlineData(PlcBlockEditMethods.DeleteInterfaceMember)]
    [InlineData(PlcBlockEditMethods.UpdateDbMember)]
    public void EditsOfDifferentMembersDoNotSupersedeEachOther(string later)
    {
        var first = Item(PlcBlockEditMethods.UpdateInterfaceMember, "Speed");
        Assert.False(Supersedes(Item(later, "Ramp"), first));
        Assert.False(Supersedes(Item(PlcBlockEditMethods.UpdateInterfaceMember, "Speed"), Item(later, "Ramp")));
        // A sibling whose name starts like the member is another member.
        Assert.False(Supersedes(Item(later, "SpeedMax"), first));
    }

    [Theory]
    [InlineData(PlcBlockEditMethods.AddInterfaceMember)]
    [InlineData(PlcBlockEditMethods.UpdateInterfaceMember)]
    [InlineData(PlcBlockEditMethods.DeleteInterfaceMember)]
    [InlineData(PlcBlockEditMethods.UpdateDbMember)]
    public void ALaterEditOfTheSameOrAnEnclosingMemberSupersedes(string later)
    {
        Assert.True(Supersedes(Item(later, "speed"), Item(PlcBlockEditMethods.AddInterfaceMember, "Speed")));
        Assert.True(Supersedes(Item(later, "Limits"), Item(PlcBlockEditMethods.UpdateDbMember, "Limits.regHyst")));
        Assert.False(Supersedes(Item(later, "Limits.regHyst"), Item(PlcBlockEditMethods.DeleteInterfaceMember, "Limits")));
        Assert.True(Supersedes(Item(later, "Values[1]"), Item(PlcBlockEditMethods.UpdateDbMember, "Values[1]")));
        Assert.False(Supersedes(Item(later, "Values[2]"), Item(PlcBlockEditMethods.UpdateDbMember, "Values[1]")));
        Assert.True(Supersedes(Item(later, "Values"), Item(PlcBlockEditMethods.UpdateDbMember, "Values[1]")));
        Assert.False(Supersedes(Item(later, "Values[1]"), Item(PlcBlockEditMethods.UpdateDbMember, "Values[10]")));
    }

    [Fact]
    public void ARenameOwnsTheOldAndTheNewName()
    {
        var rename = Item(PlcBlockEditMethods.UpdateInterfaceMember, "Speed", r => r.NewName = "Velocity");
        Assert.True(Supersedes(Item(PlcBlockEditMethods.UpdateInterfaceMember, "Velocity"), rename));
        Assert.True(Supersedes(rename, Item(PlcBlockEditMethods.AddInterfaceMember, "Velocity")));
        Assert.False(Supersedes(rename, Item(PlcBlockEditMethods.AddInterfaceMember, "Ramp")));
    }

    [Theory]
    [InlineData("update_block_logic")]
    [InlineData("set_network_text")]
    [InlineData("rename_members")]
    [InlineData("delete_block")]
    public void WholeBlockItemsAndMemberEditsStillSupersedeEachOther(string wholeBlock)
    {
        var member = Item(PlcBlockEditMethods.AddInterfaceMember, "Ramp");
        Assert.True(Supersedes(Item(wholeBlock), member));
        Assert.True(Supersedes(Item(PlcBlockEditMethods.DeleteInterfaceMember, "Ramp"), Item(wholeBlock)));
        // update_db_member writes one attribute natively and, as before, does not supersede whole-block items.
        Assert.False(Supersedes(Item(PlcBlockEditMethods.UpdateDbMember, "Ramp"), Item(wholeBlock)));
        Assert.True(Supersedes(Item(wholeBlock), Item(PlcBlockEditMethods.UpdateDbMember, "Ramp")));
    }

    [Fact]
    public void ABlockGroupDeleteAndOtherBlocksBehaveAsBefore()
    {
        var member = Item(PlcBlockEditMethods.UpdateInterfaceMember, "Speed");
        var groupDelete = Item("delete_block_group", target: new(PlcNameRules.BlockGroup, "PLC_1", "S1", null, null, "Blocks", "PLC_1/Blocks", null),
            set: o => o.BlockPath = "PLC_1/Blocks");
        Assert.True(Supersedes(groupDelete, member));
        var other = Item(PlcBlockEditMethods.UpdateInterfaceMember, "Speed", o => o.BlockPath = "PLC_1/Blocks/DB_Other", Block("PLC_1/Blocks/DB_Other"));
        Assert.False(Supersedes(other, member));
    }

    [Fact]
    public void UndeterminedTargetsSupersedePerMemberToo()
    {
        var target = Block("PLC_1/DB_Settings");
        var first = Item(PlcBlockEditMethods.UpdateInterfaceMember, "Speed", o => o.BlockPath = "PLC_1/DB_Settings", target);
        Assert.False(Supersedes(Item(PlcBlockEditMethods.UpdateInterfaceMember, "Ramp", o => o.BlockPath = "PLC_1/DB_Settings", target), first));
        Assert.True(Supersedes(Item(PlcBlockEditMethods.UpdateInterfaceMember, "Speed", o => o.BlockPath = "PLC_1/DB_Settings", target), first));
        var earlierLogic = Item("update_block_logic", set: o => o.BlockPath = "PLC_1/DB_Settings", target: target);
        Assert.True(Supersedes(Item(PlcBlockEditMethods.UpdateInterfaceMember, "Ramp", o => o.BlockPath = "PLC_1/DB_Settings", target), earlierLogic));
    }

    [Fact]
    public void TypeMemberEditsSupersedePerMember()
    {
        var type = Type("PLC_1/Types/UDT_Settings");
        PlcOperationRequest TypeItem(string operation, string? member)
            => Item(operation, member, o => { o.BlockPath = null; o.TypePath = "PLC_1/Types/UDT_Settings"; }, type);

        var first = TypeItem(PlcBlockEditMethods.UpdateInterfaceMember, "Scaling.valueMin");
        Assert.False(Supersedes(TypeItem(PlcBlockEditMethods.UpdateInterfaceMember, "Scaling.valueMax"), first));
        Assert.True(Supersedes(TypeItem(PlcBlockEditMethods.DeleteInterfaceMember, "Scaling"), first));
        Assert.True(Supersedes(TypeItem("update_type_content", null), first));
        Assert.True(Supersedes(TypeItem(PlcBlockEditMethods.AddInterfaceMember, "Offset"), TypeItem("update_type_content", null)));
    }
}
