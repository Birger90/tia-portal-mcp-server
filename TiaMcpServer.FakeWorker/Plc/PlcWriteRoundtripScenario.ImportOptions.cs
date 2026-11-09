using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// Audit N7/N8 in the <c>plc-write-roundtrip</c> fixture: <c>swImportOptions</c> and <c>documentImportOption</c> are combined on
/// stand-in flags enums through the shared <see cref="PlcImportOptionNames.Combine"/> (the values are the fixture's, not Siemens').
/// A SimaticML document containing <c>fake:structural-change</c> is refused like an import aborted on a structurally changed
/// referenced object unless <c>IgnoreStructuralChanges</c> is passed; an SD document containing <c>fake:inactive-culture</c> is
/// refused unless a culture option is passed. This models shapes only; it is no evidence of Siemens behavior.
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    [Flags]
    private enum FakeSwImportOptions
    {
        None = 0,
        IgnoreStructuralChanges = 1,
        IgnoreMissingReferencedObjects = 2,
        IgnoreUnitAttributes = 4,
    }

    [Flags]
    private enum FakeImportDocumentOptions
    {
        None = 0,
        Override = 1,
        SkipInactiveCultures = 2,
        ActivateInactiveCultures = 4,
    }

    private const string StructuralChangeMarker = "fake:structural-change";
    private const string InactiveCultureMarker = "fake:inactive-culture";

    private static void ApplySwImportOptions(IReadOnlyList<string>? names, string? content)
    {
        var options = FakeSwImportOptions.None;
        if (names is { Count: > 0 })
        {
            try
            {
                options = (FakeSwImportOptions)PlcImportOptionNames.Combine(typeof(FakeSwImportOptions), names, "swImportOptions");
            }
            catch (RuntimeBindingException exception)
            {
                Throw(exception.Category, exception.Message);
            }
        }

        if (content?.Contains(StructuralChangeMarker, StringComparison.Ordinal) == true && !options.HasFlag(FakeSwImportOptions.IgnoreStructuralChanges))
            Throw(WorkerFailureCategories.WorkerOperationFailed,
                "The import was aborted: a referenced object changed structurally (fixture). Retry with swImportOptions [\"IgnoreStructuralChanges\"] if instance data may be lost.");
    }

    private static void ApplyDocumentImportOption(string? cultureOption, string? content)
    {
        FakeImportDocumentOptions options;
        try
        {
            options = (FakeImportDocumentOptions)PlcImportOptionNames.DocumentOptions(typeof(FakeImportDocumentOptions), cultureOption);
        }
        catch (RuntimeBindingException exception)
        {
            Throw(exception.Category, exception.Message);
            return;
        }

        if (content?.Contains(InactiveCultureMarker, StringComparison.Ordinal) == true
            && (options & (FakeImportDocumentOptions.SkipInactiveCultures | FakeImportDocumentOptions.ActivateInactiveCultures)) == 0)
            Throw(WorkerFailureCategories.WorkerOperationFailed,
                "TIA Portal returned SIMATIC SD import state 'Failure': the document uses a culture that is not active in the project (fixture).");
    }
}
