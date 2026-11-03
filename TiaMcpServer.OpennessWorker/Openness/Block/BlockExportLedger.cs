using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// Counts this worker's exports of each PLC block in the current binding, for the V21 pitfall reported by
/// rickgaiser/TiaMcp (not verified on V21): TIA Portal terminates when it overwrites a block with a textual interface
/// that was exported more than once since the project was opened or the block was last written. The counting rules
/// follow that report (its <c>ExportsSinceImport</c>); the code is this fork's.
/// <list type="bullet">
/// <item>A new engineering root resets the ledger (<c>TiaPortalSession.SetActiveContext</c>): a project this worker opened,
/// created or opened as a local session starts every count at zero; any other root (an already open project, a
/// re-selected one) makes every count unknown.</item>
/// <item>Every export call of a block counts once: a SimaticML bundle (<c>Export</c> plus <c>ExportAsDocuments</c>),
/// a SimaticML document alone, the post-import <c>ExportAsDocuments</c> check and external-source generation (only
/// the requested block: the dependency closure of a <c>withDependencies</c> source is not counted). An unknown count
/// stays unknown.</item>
/// <item>A returned import of the block (<c>update_block_logic</c> through SimaticML, SIMATIC SD or external source)
/// sets its count to a known zero.</item>
/// <item>A VCI export or import makes every count unknown: its leaves are not tracked one by one.</item>
/// </list>
/// Counts follow PLC software and block names (case-insensitive). A renamed, deleted or re-created block keeps the
/// name's count (an over-count). Two blocks of the same name in different software units share one count, so a write
/// of one restarts the count of the other too (an under-count). Exports by other Openness clients, technology object
/// exports and library operations are not counted. The guards never depend on the count; it is message text only.
/// </summary>
internal sealed class BlockExportLedger
{
    internal static BlockExportLedger Shared { get; } = new();

    private readonly object _sync = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _written = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _textual = new(StringComparer.OrdinalIgnoreCase);
    private bool _openedHere;

    /// <summary>A new engineering root: counts start at a known zero only when this worker just opened it.</summary>
    public void Reset(bool openedHere)
    {
        lock (_sync)
        {
            _counts.Clear();
            _written.Clear();
            _textual.Clear();
            _openedHere = openedHere;
        }
    }

    /// <summary>Every count becomes unknown (a VCI scope exported or imported blocks); textual-interface flags stay.</summary>
    public void ForgetCounts()
    {
        lock (_sync)
        {
            _counts.Clear();
            _written.Clear();
            _openedHere = false;
        }
    }

    public void RecordExport(string plcName, string blockName)
    {
        lock (_sync)
        {
            var key = Key(plcName, blockName);
            if (CountOf(key) is { } count) _counts[key] = count + 1;
        }
    }

    public void RecordTextualInterface(string plcName, string blockName, bool textualInterface)
    {
        lock (_sync) _textual[Key(plcName, blockName)] = textualInterface;
    }

    /// <summary>The block was written (its import returned): its count is a known zero again.</summary>
    public void RecordWrite(string plcName, string blockName)
    {
        lock (_sync)
        {
            var key = Key(plcName, blockName);
            _counts[key] = 0;
            _written.Add(key);
        }
    }

    public PlcBlockExportStateInfo Read(string plcName, string blockName)
    {
        lock (_sync)
        {
            var key = Key(plcName, blockName);
            var count = CountOf(key);
            return new PlcBlockExportStateInfo
            {
                PlcName = plcName,
                BlockName = blockName,
                ExportCount = count,
                Basis = count is null ? PlcBlockExportStateMethods.Unknown
                    : _written.Contains(key) ? PlcBlockExportStateMethods.WrittenByThisServer
                    : PlcBlockExportStateMethods.OpenedByThisServer,
                TextualInterface = _textual.TryGetValue(key, out var textual) ? textual : null,
            };
        }
    }

    private int? CountOf(string key)
        => _counts.TryGetValue(key, out var count) ? count : _openedHere ? 0 : null;

    private static string Key(string plcName, string blockName) => plcName + "\n" + blockName;
}
