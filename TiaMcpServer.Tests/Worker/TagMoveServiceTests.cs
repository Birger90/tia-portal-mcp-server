using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Worker;

/// <summary>move_tag against the offline object doubles: re-create, delete, rename, read back.</summary>
public sealed class TagMoveServiceTests
{
    private static (SiemensProject Project, PlcTagTable Source, PlcTagTable Target) Fixture()
    {
        var project = new SiemensProject { Path = new FileInfo(@"C:\p\p.ap21") };
        var plc = new PlcSoftware { Name = "PLC_1" };
        var device = new Device { Name = "Device" };
        device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = plc } });
        project.Devices.Items.Add(device);
        var source = plc.TagTableGroup.TagTables.Create("Default");
        var target = plc.TagTableGroup.TagTables.Create("Motors");
        var tag = source.Tags.Create("Speed", "Int", "%MW10");
        tag.ExternalAccessible = true;
        tag.ExternalVisible = false;
        tag.ExternalWritable = true;
        return (project, source, target);
    }

    private static PlcTagMoveRequestInfo Move(string target = "Motors") => new()
    {
        PlcName = "PLC_1", TableName = "Default", Name = "Speed", TargetTableName = target, TemporaryName = "__mcp_move_test",
    };

    [Fact]
    public void Move_RecreatesTheTagWithItsDocumentedAttributesAndDeletesTheSource()
    {
        var (project, source, target) = Fixture();

        var result = TagMutationService.MoveTag(project, Move());

        Assert.Empty(source.Tags);
        var moved = Assert.Single(target.Tags);
        Assert.Equal(("Speed", "Int", "%MW10"), (moved.Name, moved.DataTypeName, moved.LogicalAddress));
        Assert.Equal((true, false, true), (moved.ExternalAccessible, moved.ExternalVisible, moved.ExternalWritable));
        Assert.Equal(("Default", "Motors", "/"), (result.SourceTableName, result.TargetTableName, result.TargetFolderPath));
        Assert.Equal(6, result.Steps.Count);
        Assert.DoesNotContain(result.NotCopied, entry => entry.StartsWith("Comment", StringComparison.Ordinal));
    }

    [Fact]
    public void Move_CopiesTheCommentPerLanguageAndReadsItBack()
    {
        var (project, source, target) = Fixture();
        source.Tags.Single().CommentText.With("en-US", "Conveyor speed").With("de-DE", "Bandgeschwindigkeit").With("fr-FR", "");
        target.Tags.CommentCultures.AddRange(new[] { "en-US", "de-DE", "fr-FR" });

        var result = TagMutationService.MoveTag(project, Move());

        var moved = Assert.Single(target.Tags);
        Assert.Equal("Conveyor speed", moved.Comment.Items.Find(Language.Of("en-US"))!.Text);
        Assert.Equal("Bandgeschwindigkeit", moved.Comment.Items.Find(Language.Of("de-DE"))!.Text);
        Assert.Contains(result.Steps, step => step.Contains("2 language(s): en-US, de-DE", StringComparison.Ordinal));
        Assert.DoesNotContain(result.NotCopied, entry => entry.StartsWith("Comment", StringComparison.Ordinal));
    }

    [Fact]
    public void Move_ReportsALanguageTheNewTagHasNoItemFor()
    {
        var (project, source, target) = Fixture();
        source.Tags.Single().CommentText.With("en-US", "Speed").With("it-IT", "Velocita");
        target.Tags.CommentCultures.Add("en-US");

        var result = TagMutationService.MoveTag(project, Move());

        Assert.Equal("Speed", Assert.Single(target.Tags).Comment.Items.Find(Language.Of("en-US"))!.Text);
        Assert.Contains(result.NotCopied, entry => entry.StartsWith("Comment (it-IT)", StringComparison.Ordinal));
    }

    [Fact]
    public void Move_CommentThatDoesNotStickFailsBeforeTheSourceIsDeleted()
    {
        var (project, source, target) = Fixture();
        source.Tags.Single().CommentText.With("en-US", "Speed");
        target.Tags.CommentCultures.Add("en-US");
        target.Tags.Created = tag => tag.CommentText.Items.Single().TextFailure = new EngineeringException("comment is read-only here");

        var ex = Assert.Throws<WorkerOperationException>(() => TagMutationService.MoveTag(project, Move()));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
        Assert.Contains("was not deleted", ex.Message, StringComparison.Ordinal);
        Assert.Single(source.Tags);
        Assert.Empty(target.Tags);
    }

    [Fact]
    public void Move_UnreadableCommentIsReportedAndTheMoveContinues()
    {
        var (project, source, target) = Fixture();
        source.Tags.Single().CommentFailure = new EngineeringException("comment unavailable");

        var result = TagMutationService.MoveTag(project, Move());

        Assert.Single(target.Tags);
        Assert.Contains(result.NotCopied, entry => entry.StartsWith("Comment: unreadable", StringComparison.Ordinal));
    }

    [Fact]
    public void Move_SourceDeleteFailureRemovesTheTemporaryTagAgain()
    {
        var (project, source, target) = Fixture();
        source.Tags.Single().DeleteFailure = new EngineeringTargetInvocationException("delete rejected");

        var ex = Assert.Throws<WorkerOperationException>(() => TagMutationService.MoveTag(project, Move()));

        Assert.Equal(WorkerFailureCategories.EngineeringRejected, ex.FailureCategory);
        Assert.Contains("The temporary tag was deleted again.", ex.Message, StringComparison.Ordinal);
        Assert.Single(source.Tags);
        Assert.Empty(target.Tags);
    }

    [Fact]
    public void Move_RenameFailureNamesTheTemporaryTagThatHoldsTheMove()
    {
        var (project, source, target) = Fixture();
        target.Tags.Created = tag => tag.NameWriteFailure = new EngineeringTargetInvocationException("rename rejected");

        var ex = Assert.Throws<WorkerOperationException>(() => TagMutationService.MoveTag(project, Move()));

        Assert.Equal(WorkerFailureCategories.EngineeringRejected, ex.FailureCategory);
        Assert.Contains("remains as '__mcp_move_test'", ex.Message, StringComparison.Ordinal);
        Assert.Empty(source.Tags);
        Assert.Equal("__mcp_move_test", Assert.Single(target.Tags).Name);
    }

    [Fact]
    public void Move_IgnoredRenameFailsPostcondition()
    {
        var (project, source, target) = Fixture();
        target.Tags.Created = tag => tag.IgnoreNameWrites = true;

        var ex = Assert.Throws<WorkerOperationException>(() => TagMutationService.MoveTag(project, Move()));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, ex.FailureCategory);
        Assert.Contains("still named '__mcp_move_test'", ex.Message, StringComparison.Ordinal);
        Assert.Empty(source.Tags);
    }

    [Fact]
    public void Move_TemporaryCreateFailureChangesNothing()
    {
        var (project, source, target) = Fixture();
        target.Tags.CreateFailure = new EngineeringTargetInvocationException("create rejected");

        Assert.Throws<EngineeringTargetInvocationException>(() => TagMutationService.MoveTag(project, Move()));

        Assert.Single(source.Tags);
        Assert.Empty(target.Tags);
    }

    [Fact]
    public void Move_SameTableIsValidationErrorWithoutChange()
    {
        var (project, source, _) = Fixture();
        var ex = Assert.Throws<WorkerOperationException>(() => TagMutationService.MoveTag(project, Move("Default")));
        Assert.Equal(WorkerFailureCategories.ValidationError, ex.FailureCategory);
        Assert.Single(source.Tags);
    }

    [Fact]
    public void Move_TemporaryNameInUseIsRefusedBeforeAnyChange()
    {
        var (project, source, target) = Fixture();
        target.Tags.Create("__mcp_move_test", "Bool", "");
        var ex = Assert.Throws<WorkerOperationException>(() => TagMutationService.MoveTag(project, Move()));
        Assert.Equal(WorkerFailureCategories.ValidationError, ex.FailureCategory);
        Assert.Single(source.Tags);
    }

    [Fact]
    public void Move_UnreadableFlagIsReportedAsNotCopied()
    {
        var (project, source, _) = Fixture();
        source.Tags.Single().ExternalVisibleFailure = new EngineeringException("not supported here");
        var result = TagMutationService.MoveTag(project, Move());
        Assert.Null(result.ExternalVisible);
        Assert.Contains(result.NotCopied, entry => entry.StartsWith("ExternalVisible", StringComparison.Ordinal));
    }

    [Fact]
    public void Move_MissingTargetTableIsTargetNotFound()
    {
        var (project, source, _) = Fixture();
        var ex = Assert.Throws<WorkerOperationException>(() => TagMutationService.MoveTag(project, Move("Missing")));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
        Assert.Single(source.Tags);
    }
}
