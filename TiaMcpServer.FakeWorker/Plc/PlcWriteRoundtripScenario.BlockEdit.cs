using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// The granular block editing methods of the <c>plc-write-roundtrip</c> fixture. PLC_1 additionally
/// holds the global DB <c>Settings</c>, the instance DB <c>FB_Motor_DB</c> of <c>FB_Motor</c>, the
/// fail-safe FB <c>Safety_FB</c> (ProgrammingLanguage F_LAD) and the know-how-protected FC
/// <c>Protected_FC</c>; <c>Settings</c>, <c>FB_Motor</c>, <c>Safety_FB</c> and <c>UDT_Settings</c> carry
/// SimaticML exports in the shape of the repository's V21 fixtures. DB start values not yet written
/// come from the document's own StartValue elements. A start value of <c>IgnoredByPortal</c> is kept
/// unchanged, as Portal would silently ignore a write.
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    private const string IgnoredStartValue = "IgnoredByPortal";

    private readonly Dictionary<string, string> _instanceOf = new(Names) { ["FB_Motor_DB"] = "FB_Motor" };
    private readonly Dictionary<string, string?> _dbValues = new(Names);

    /// <summary>
    /// K01/K02/K20 model: a SetPoint change leaves the DB inconsistent (live V21 2026-11-01: a Retain change did) and so does
    /// a renumber to <see cref="InconsistentNumber"/> (live D2: renumber_block, then move_block), so every export of the block
    /// (get_block_content, get_block_interface, get_db_values, the copy/move round trip) is refused with TIA Portal's text and
    /// the worker's IsConsistent hint until compiled.
    /// </summary>
    private readonly HashSet<string> _inconsistent = new(Names);

    private const int InconsistentNumber = 4242;

    private void RequireConsistentForExport(string path)
    {
        if (_inconsistent.Contains(path))
            Throw(WorkerFailureCategories.WorkerOperationFailed,
                "TIA Portal operation failed: Inconsistent blocks and PLC data types (UDT) cannot be exported. " + PlcExportConsistency.BlockHint(path));
    }

    internal const string SettingsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Document>
          <Engineering version="V21" />
          <SW.Blocks.GlobalDB ID="0">
            <AttributeList>
              <AutoNumber>true</AutoNumber>
              <Interface>
                <Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">
                  <Section Name="Static">
                    <Member Name="Speed" Datatype="Int">
                      <Comment>
                        <MultiLanguageText Lang="en-US">Set speed</MultiLanguageText>
                      </Comment>
                      <StartValue>100</StartValue>
                    </Member>
                    <Member Name="Limits" Datatype="Struct">
                      <Member Name="regMin" Datatype="Int">
                        <StartValue>-500</StartValue>
                      </Member>
                      <Member Name="regMax" Datatype="Int">
                        <StartValue>30000</StartValue>
                      </Member>
                    </Member>
                    <Member Name="Config" Datatype="&quot;UDT_Settings&quot;" />
                    <Member Name="Buffer" Datatype="Array[0..3] of Real" />
                  </Section>
                </Sections>
              </Interface>
              <MemoryLayout>Optimized</MemoryLayout>
              <Name>Settings</Name>
              <Namespace />
              <Number>3</Number>
              <ProgrammingLanguage>DB</ProgrammingLanguage>
            </AttributeList>
            <ObjectList>
              <MultilingualText ID="1" CompositionName="Comment" />
              <MultilingualText ID="2" CompositionName="Title" />
            </ObjectList>
          </SW.Blocks.GlobalDB>
        </Document>
        """;

    internal const string MotorXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Document>
          <Engineering version="V21" />
          <SW.Blocks.FB ID="0">
            <AttributeList>
              <AutoNumber>true</AutoNumber>
              <Interface>
                <Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">
                  <Section Name="Input">
                    <Member Name="Enable" Datatype="Bool" />
                  </Section>
                  <Section Name="Output" />
                  <Section Name="InOut" />
                  <Section Name="Static">
                    <Member Name="Count" Datatype="Int">
                      <StartValue>0</StartValue>
                    </Member>
                  </Section>
                  <Section Name="Temp" />
                  <Section Name="Constant" />
                </Sections>
              </Interface>
              <Name>FB_Motor</Name>
              <Namespace />
              <ProgrammingLanguage>SCL</ProgrammingLanguage>
              <SetENOAutomatically>false</SetENOAutomatically>
            </AttributeList>
            <ObjectList>
              <MultilingualText ID="1" CompositionName="Comment" />
              <SW.Blocks.CompileUnit ID="3" CompositionName="CompileUnits">
                <AttributeList>
                  <NetworkSource>
                    <StructuredText xmlns="http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4">
                      <Token Text="IF" UId="21" />
                      <Blank UId="22" />
                      <Access Scope="LocalVariable" UId="23">
                        <Symbol UId="24">
                          <Component Name="Enable" UId="25" />
                        </Symbol>
                      </Access>
                      <Blank UId="26" />
                      <Token Text="THEN" UId="27" />
                      <NewLine UId="28" />
                      <Access Scope="LocalVariable" UId="29">
                        <Symbol UId="30">
                          <Component Name="Count" UId="31" />
                        </Symbol>
                      </Access>
                      <Token Text=":=" UId="32" />
                      <Access Scope="LocalVariable" UId="33">
                        <Symbol UId="34">
                          <Component Name="Count" UId="35" />
                        </Symbol>
                      </Access>
                      <Token Text="+" UId="36" />
                      <Access Scope="LiteralConstant" UId="37">
                        <Constant UId="38">
                          <ConstantValue UId="39">1</ConstantValue>
                        </Constant>
                      </Access>
                      <Token Text=";" UId="40" />
                      <NewLine UId="41" />
                      <Token Text="END_IF" UId="42" />
                      <Token Text=";" UId="43" />
                    </StructuredText>
                  </NetworkSource>
                  <ProgrammingLanguage>SCL</ProgrammingLanguage>
                </AttributeList>
                <ObjectList>
                  <MultilingualText ID="4" CompositionName="Comment">
                    <ObjectList>
                      <MultilingualTextItem ID="5" CompositionName="Items">
                        <AttributeList>
                          <Culture>en-US</Culture>
                          <Text>Count pump 1 starts</Text>
                        </AttributeList>
                      </MultilingualTextItem>
                    </ObjectList>
                  </MultilingualText>
                  <MultilingualText ID="6" CompositionName="Title">
                    <ObjectList>
                      <MultilingualTextItem ID="7" CompositionName="Items">
                        <AttributeList>
                          <Culture>en-US</Culture>
                          <Text>Pump 1</Text>
                        </AttributeList>
                      </MultilingualTextItem>
                    </ObjectList>
                  </MultilingualText>
                </ObjectList>
              </SW.Blocks.CompileUnit>
              <SW.Blocks.CompileUnit ID="8" CompositionName="CompileUnits">
                <AttributeList>
                  <NetworkSource />
                  <ProgrammingLanguage>SCL</ProgrammingLanguage>
                </AttributeList>
                <ObjectList>
                  <MultilingualText ID="9" CompositionName="Comment" />
                  <MultilingualText ID="A" CompositionName="Title">
                    <ObjectList>
                      <MultilingualTextItem ID="B" CompositionName="Items">
                        <AttributeList>
                          <Culture>en-US</Culture>
                          <Text>Pump 10</Text>
                        </AttributeList>
                      </MultilingualTextItem>
                    </ObjectList>
                  </MultilingualText>
                </ObjectList>
              </SW.Blocks.CompileUnit>
              <MultilingualText ID="2" CompositionName="Title" />
            </ObjectList>
          </SW.Blocks.FB>
        </Document>
        """;

    internal const string SafetyXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Document>
          <Engineering version="V21" />
          <SW.Blocks.FB ID="0">
            <AttributeList>
              <Interface>
                <Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">
                  <Section Name="Static" />
                </Sections>
              </Interface>
              <Name>Safety_FB</Name>
              <Namespace />
              <ProgrammingLanguage>F_LAD</ProgrammingLanguage>
            </AttributeList>
          </SW.Blocks.FB>
        </Document>
        """;

    internal const string SettingsTypeXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Document>
          <Engineering version="V21" />
          <SW.Types.PlcStruct ID="0">
            <AttributeList>
              <Interface>
                <Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">
                  <Section Name="None">
                    <Member Name="Scaling" Datatype="Struct">
                      <Member Name="Type" Datatype="Bool">
                        <Comment>
                          <MultiLanguageText Lang="en-US">0- multiply, 1 - scale</MultiLanguageText>
                        </Comment>
                        <StartValue>TRUE</StartValue>
                      </Member>
                      <Member Name="valueMin" Datatype="Real" />
                    </Member>
                  </Section>
                </Sections>
              </Interface>
              <Name>UDT_Settings</Name>
              <Namespace />
            </AttributeList>
          </SW.Types.PlcStruct>
        </Document>
        """;

    private void SeedBlockEditFixture(Plc plc)
    {
        plc.Root.Blocks.AddRange(new[] { ("Settings", "GlobalDB", "DB", 3), ("FB_Motor_DB", "InstanceDB", "DB", 4), ("Safety_FB", "FB", "F_LAD", 5), ("Protected_FC", "FC", "SCL", 6) });
        var path = $"{plc.Software}/Blocks";
        _content[Key($"{path}/Settings", SourceFormatNames.Xml)] = Bundle("Settings", SettingsXml, "DATA_BLOCK \"Settings\"\nEND_DATA_BLOCK\n");
        _content[Key($"{path}/Motors/FB_Motor", SourceFormatNames.Xml)] = Bundle("FB_Motor", MotorXml, "FUNCTION_BLOCK \"FB_Motor\"\nEND_FUNCTION_BLOCK\n");
        _content[Key($"{path}/Safety_FB", SourceFormatNames.Xml)] = Bundle("Safety_FB", SafetyXml, "FUNCTION_BLOCK \"Safety_FB\"\nEND_FUNCTION_BLOCK\n");
        _content[Key($"{plc.Software}/Types/UDT_Settings", SourceFormatNames.Xml)] = SettingsTypeXml;
    }

    private static string Bundle(string name, string xml, string source)
        => $"--- FILE: {name}.xml ---\n{xml}\n--- FILE: {name}.s7dcl ---\n{source}";

    private string BlockEdit(WorkerRequest request)
    {
        var edit = request.PlcBlockEdit ?? new PlcBlockEditRequestInfo();
        return request.Method switch
        {
            PlcBlockEditMethods.GetBlockInterface => Ok(BlockInterface(edit.BlockPath!)),
            PlcBlockEditMethods.GetTypeInterface => Ok(TypeInterface(edit.TypePath!)),
            PlcBlockEditMethods.GetDbValues => Ok(DbValues(edit)),
            PlcBlockEditMethods.UpdateDbMember => Ok(UpdateDbMember(edit)),
            PlcBlockEditMethods.MoveTagTable => Ok(MoveTable(edit)),
            PlcBlockEditMethods.GetDbSnapshot or PlcBlockEditMethods.CreateDbSnapshot => Ok(DbSnapshot(request.Method, edit)),
            PlcBlockEditMethods.RenameTags or PlcBlockEditMethods.RenameObjects => Ok(BatchRename(request.Method, edit)),
            PlcBlockEditMethods.ReplaceTagComments => Ok(ReplaceTagComments(edit)),
            PlcBlockEditMethods.ExportTagTable => Ok(ExportTagTable(edit)),
            PlcBlockEditMethods.ImportTagTable => Ok(ImportTagTable(edit)),
            PlcBlockEditMethods.ExportOpcUaXml => Ok(ExportOpcUaXml(edit)),
            SimaticSdMethods.CreateBlockFromDocument => Ok(CreateBlockFromDocument(edit)),
            _ => Ok(EditBlock(request.Method, edit)),
        };
    }

    private PlcBlockInterfaceInfo BlockInterface(string blockPath)
    {
        var (plc, _, block, path) = ResolveBlock(blockPath);
        RequireConsistentForExport(path);
        var bundle = Content(plc, path, SourceFormatNames.Xml);
        var info = new PlcBlockInterfaceInfo { Block = Identity(plc, path), ContentHash = ContentHashRules.Compute(SourceFormatNames.Xml, bundle) };
        if (!PlcBlockBundleText.TryGetSimaticMl(bundle, out _, out var xml))
        {
            info.Messages.Add("The export has no SimaticML document.");
            return info;
        }

        try
        {
            var document = SimaticMlInterfaceDocument.Parse(xml);
            info.DocumentElement = document.DocumentElement;
            info.IsSafety = document.IsSafety;
            info.Sections = document.ReadSections();
        }
        catch (PlcInterfaceEditException exception)
        {
            info.Messages.Add(exception.Message);
        }

        return info;
    }

    private PlcTypeInterfaceInfo TypeInterface(string typePath)
    {
        var (plc, type) = ResolveType(typePath);
        var xml = Content(plc, $"{plc.Software}/Types/{type}", SourceFormatNames.Xml);
        var info = new PlcTypeInterfaceInfo { PlcName = plc.Software, TypePath = typePath, Name = type, ContentHash = ContentHashRules.Compute(SourceFormatNames.Xml, xml) };
        try
        {
            var document = SimaticMlInterfaceDocument.Parse(xml);
            info.DocumentElement = document.DocumentElement;
            info.Sections = document.ReadSections();
        }
        catch (PlcInterfaceEditException exception)
        {
            info.Messages.Add(exception.Message);
        }

        return info;
    }

    private PlcDbValuesInfo DbValues(PlcBlockEditRequestInfo edit)
    {
        var (plc, _, block, path) = ResolveBlock(edit.BlockPath!);
        RequireConsistentForExport(path);
        var identity = Identity(plc, path);
        var roots = DbRoots(plc, identity);
        var subtree = edit.MemberPath is null ? null : PlcMemberPath.Parse(edit.MemberPath, allowIndices: true).ToFindPath();
        var tree = PlcDbValueTree.Build(roots, name => TypeMembers(plc, name), subtree);
        var result = new PlcDbValuesInfo { Block = identity, MemberPath = subtree, IsComplete = tree.IsComplete, Messages = tree.Messages.ToList() };
        foreach (var entry in tree.Entries)
        {
            result.Values.Add(new PlcDbValueInfo
            {
                Path = entry.Path, DataType = entry.DataType, Kind = entry.Kind, Message = entry.Message,
                StartValue = entry.Kind == PlcDbValueKinds.Value ? StartValue(path, entry.Path, roots, plc) : null,
                Attributes = edit.WithAttributes == true && entry.Kind == PlcDbValueKinds.Value ? Attributes(path, entry.Path) : null,
            });
        }

        if (subtree is not null && !tree.SubtreeFound)
            result.Values.Add(new PlcDbValueInfo { Path = subtree, Kind = PlcDbValueKinds.Missing, Message = "Interface.Members.Find returned no member for this path." });
        return result;
    }

    private PlcDbMemberUpdateResultInfo UpdateDbMember(PlcBlockEditRequestInfo edit)
    {
        var (plc, _, dbName, path) = ResolveBlock(edit.BlockPath!);
        RequireFailSafeImportPermitted(plc, dbName, null, PlcBlockEditMethods.UpdateDbMember);
        var identity = Identity(plc, path);
        var roots = DbRoots(plc, identity);
        var member = PlcMemberPath.Parse(edit.MemberPath!, allowIndices: true).ToFindPath();
        var tree = PlcDbValueTree.Build(roots, name => TypeMembers(plc, name), member);
        if (!tree.SubtreeFound) Throw(WorkerFailureCategories.TargetNotFound, $"Member '{member}' was not found in '{path}'.");
        var result = new PlcDbMemberUpdateResultInfo { Block = identity, MemberPath = member };
        void Set(string attribute, string? requested, Func<string?> read, Action<string> write)
        {
            if (requested is null) return;
            var change = new PlcDbAttributeChangeInfo { Attribute = attribute, Previous = read(), Requested = requested };
            if (requested != IgnoredStartValue) write(requested);
            change.Observed = read();
            result.Changes.Add(change);
        }

        Set("StartValue", edit.StartValue, () => StartValue(path, member, roots, plc), value => _dbValues[$"{path}|{member}"] = value);
        foreach (var (name, flag) in new[] { ("ExternalAccessible", edit.ExternalAccessible), ("ExternalVisible", edit.ExternalVisible), ("ExternalWritable", edit.ExternalWritable), ("Retain", edit.Retain), ("SetPoint", edit.SetPoint) })
            Set(name, flag is null ? null : flag.Value ? "True" : "False", () => _dbValues.GetValueOrDefault($"{path}|{member}|{name}") ?? (name is "Retain" or "SetPoint" ? "False" : "True"),
                value => _dbValues[$"{path}|{member}|{name}"] = value);
        if (edit.SetPoint is not null) _inconsistent.Add(path);
        if (result.Changes.FirstOrDefault(c => c.Requested == IgnoredStartValue) is { } ignored)
            Throw(WorkerFailureCategories.PostconditionFailed, $"TIA Portal kept the previous value of StartValue ('{ignored.Observed}') on '{path}.{member}'.");
        return result;
    }

    private PlcBlockEditResultInfo EditBlock(string method, PlcBlockEditRequestInfo edit)
    {
        if (method == PlcBlockEditMethods.CreateInstanceDb)
        {
            var address = BlockAddress.Parse(edit.BlockPath!);
            var plc = FindPlc(address.PlcName);
            var group = address.IsDeterministic ? Walk(plc.Root, address.FolderPath) : plc.Root;
            if (AllBlocks(plc.Root).Any(b => Names.Equals(b.Name, address.BlockName))) throw PortalRejection("The block already exists.");
            if (!AllBlocks(plc.Root).Any(b => Names.Equals(b.Name, edit.InstanceOfName) && b.Type == "FB"))
                throw PortalRejection($"An instance DB cannot be created for '{edit.InstanceOfName}'.");
            var number = edit.Number ?? Enumerable.Range(1, 60000).First(n => !AllBlocks(plc.Root).Any(b => b.Type is "GlobalDB" or "InstanceDB" && b.Number == n));
            group.Blocks.Add((address.BlockName, "InstanceDB", "DB", number));
            _instanceOf[address.BlockName] = edit.InstanceOfName!;
            var created = BlocksPath(plc, address.FolderPath, address.BlockName);
            return new PlcBlockEditResultInfo { Operation = method, Block = Identity(plc, created) };
        }

        var (owner, sourceGroup, name, source) = ResolveBlock(edit.BlockPath!);
        var index = sourceGroup.Blocks.FindIndex(b => b.Name == name);
        var entry = sourceGroup.Blocks[index];
        var folders = source.Split('/').Skip(2).SkipLast(1).ToList();
        RequireFailSafeImportPermitted(owner, name, null, method);
        switch (method)
        {
            case PlcBlockEditMethods.RenameBlock:
                if (AllBlocks(owner.Root).Any(b => Names.Equals(b.Name, edit.NewName))) throw PortalRejection("The block name is taken.");
                sourceGroup.Blocks[index] = entry with { Name = edit.NewName! };
                return Result(method, owner, source, MoveContent(source, BlocksPath(owner, folders, edit.NewName!)));
            case PlcBlockEditMethods.RenumberBlock:
                sourceGroup.Blocks[index] = entry with { Number = edit.Number ?? entry.Number };
                if (edit.Number == InconsistentNumber) _inconsistent.Add(source);
                return Result(method, owner, source, source);
            case PlcBlockEditMethods.CopyBlock:
            case PlcBlockEditMethods.MoveBlock:
            {
                if (name == "Protected_FC") Throw(WorkerFailureCategories.TargetKindUnsupported, $"'{source}' is know-how protected; it is not copied or moved.");
                // The round trip's export is the move's own first step: TIA Portal refuses it for an inconsistent block (K02).
                if (_inconsistent.Contains(source))
                    Throw(WorkerFailureCategories.EngineeringRejected,
                        "TIA Portal rejected the operation: Inconsistent blocks and PLC data types (UDT) cannot be exported. " + PlcExportConsistency.BlockHint(source));
                var targetFolders = edit.TargetGroupPath is null ? folders : BlockAddress.Parse(edit.TargetGroupPath.TrimEnd('/') + "/_").FolderPath.ToList();
                var target = Walk(owner.Root, targetFolders);
                if (method == PlcBlockEditMethods.MoveBlock)
                {
                    if (target == sourceGroup) Throw(WorkerFailureCategories.ValidationError, $"'{source}' is already in that group.");
                    sourceGroup.Blocks.RemoveAt(index);
                    target.Blocks.Add(entry);
                    return Result(method, owner, source, MoveContent(source, BlocksPath(owner, targetFolders, name)));
                }

                if (AllBlocks(owner.Root).Any(b => Names.Equals(b.Name, edit.NewName))) throw PortalRejection("The block name is taken.");
                var number = edit.Number ?? Enumerable.Range(1, 60000).First(n => !AllBlocks(owner.Root).Any(b => b.Type == entry.Type && b.Number == n));
                target.Blocks.Add((edit.NewName!, entry.Type, entry.Language, number));
                var copy = BlocksPath(owner, targetFolders, edit.NewName!);
                if (_content.TryGetValue(Key(source, SourceFormatNames.Xml), out var bundle))
                    _content[Key(copy, SourceFormatNames.Xml)] = bundle;
                return Result(method, owner, source, copy);
            }

            default:
                return Throw<PlcBlockEditResultInfo>(WorkerFailureCategories.ValidationError, $"unexpected block edit method '{method}'");
        }
    }

    private PlcTagTableMoveResultInfo MoveTable(PlcBlockEditRequestInfo edit)
    {
        var plc = FindPlc(edit.PlcName);
        var source = Folder(edit.FolderPath);
        var target = Folder(edit.TargetFolderPath);
        var table = plc.Tables.FirstOrDefault(t => Names.Equals(t.FolderPath, source) && Names.Equals(t.Name, edit.TableName))
            ?? Throw<TagTableInfo>(WorkerFailureCategories.TargetNotFound, $"Tag table '{edit.TableName}' was not found in '{source}'.");
        if (table.IsDefault) Throw(WorkerFailureCategories.ValidationError, $"'{table.Name}' is the default tag table; it cannot be moved.");
        if (Names.Equals(source, target)) Throw(WorkerFailureCategories.ValidationError, $"Tag table '{table.Name}' is already in '{target}'.");
        table.FolderPath = target;
        return new PlcTagTableMoveResultInfo
        {
            PlcName = plc.Software, TableName = table.Name, SourceFolderPath = source, TargetFolderPath = target,
            TagCount = table.Tags.Count, UserConstantCount = table.UserConstants.Count,
        };
    }

    private PlcBlockEditResultInfo Result(string method, Plc plc, string source, string path)
        => new() { Operation = method, SourceBlockPath = source, Block = Identity(plc, path) };

    private string MoveContent(string from, string to)
    {
        if (_content.Remove(Key(from, SourceFormatNames.Xml), out var bundle)) _content[Key(to, SourceFormatNames.Xml)] = bundle;
        return to;
    }

    /// <summary>The identity of the block at a canonical path of <paramref name="plc"/> (never re-resolved through the PLC name).</summary>
    private PlcBlockIdentityInfo Identity(Plc plc, string canonical)
    {
        var segments = canonical.Split('/');
        var name = segments[^1];
        var entry = Walk(plc.Root, segments.Skip(2).SkipLast(1)).Blocks.First(b => Names.Equals(b.Name, name));
        return new PlcBlockIdentityInfo
        {
            PlcName = plc.Software, BlockPath = canonical, Name = entry.Name, BlockType = entry.Type, Number = entry.Number, AutoNumber = false,
            ProgrammingLanguage = entry.Language, IsKnowHowProtected = entry.Name == "Protected_FC",
            InstanceOfName = entry.Type == "InstanceDB" ? _instanceOf.GetValueOrDefault(entry.Name) : null,
        };
    }

    private IReadOnlyList<PlcInterfaceMemberInfo> DbRoots(Plc plc, PlcBlockIdentityInfo identity)
    {
        if (identity.BlockType == "InstanceDB")
            return TypeMembers(plc, identity.InstanceOfName!) ?? Throw<IReadOnlyList<PlcInterfaceMemberInfo>>(WorkerFailureCategories.TargetKindUnsupported, "The FB of the instance DB could not be exported.");
        if (identity.BlockType != "GlobalDB")
            Throw(WorkerFailureCategories.TargetKindUnsupported, $"'{identity.BlockPath}' is a {identity.BlockType}; DB values are read and written on global and instance DBs.");
        return Sections(Content(plc, identity.BlockPath, SourceFormatNames.Xml)).SelectMany(s => s.Members).ToList();
    }

    private IReadOnlyList<PlcInterfaceMemberInfo>? TypeMembers(Plc plc, string name)
    {
        if (plc.Types.Contains(name, Names))
            return Sections(Content(plc, $"{plc.Software}/Types/{name}", SourceFormatNames.Xml)).SelectMany(s => s.Members).ToList();
        var fb = UserBlocks(plc.Root, new List<string>()).FirstOrDefault(b => Names.Equals(b.Block, name));
        if (fb.Block is null) return null;
        var sections = Sections(Content(plc, BlocksPath(plc, fb.Folders, fb.Block), SourceFormatNames.Xml));
        return new[] { "Input", "Output", "InOut", "Static" }.SelectMany(section => sections.Where(s => s.Name == section).SelectMany(s => s.Members)).ToList();
    }

    private static List<PlcInterfaceSectionInfo> Sections(string content)
    {
        var xml = PlcBlockBundleText.TryGetSimaticMl(content, out _, out var document) ? document : content;
        try
        {
            return SimaticMlInterfaceDocument.Parse(xml).ReadSections();
        }
        catch (PlcInterfaceEditException)
        {
            return new List<PlcInterfaceSectionInfo>();
        }
    }

    private string? StartValue(string blockPath, string member, IReadOnlyList<PlcInterfaceMemberInfo> roots, Plc plc)
        => _dbValues.TryGetValue($"{blockPath}|{member}", out var written) ? written : DocumentStartValue(plc, roots, member);

    /// <summary>The StartValue element of the member a path names, following quoted types; array elements have none.</summary>
    private string? DocumentStartValue(Plc plc, IReadOnlyList<PlcInterfaceMemberInfo> roots, string member)
    {
        PlcInterfaceMemberInfo? current = null;
        IReadOnlyList<PlcInterfaceMemberInfo>? level = roots;
        foreach (var segment in PlcMemberPath.Parse(member, allowIndices: true).Segments)
        {
            if (level is null || segment.Index is not null) return null;
            current = level.FirstOrDefault(m => Names.Equals(m.Name, segment.Name));
            if (current is null) return null;
            level = SimaticMlInterfaceDocument.IsQuoted(current.DataType) ? TypeMembers(plc, current.DataType!.Trim('"')) : current.Members;
        }

        return current?.StartValue;
    }

    private PlcDbMemberAttributesInfo Attributes(string blockPath, string member)
    {
        bool Flag(string name, bool fallback) => _dbValues.GetValueOrDefault($"{blockPath}|{member}|{name}") is { } text ? text == "True" : fallback;
        return new PlcDbMemberAttributesInfo
        {
            ExternalAccessible = Flag("ExternalAccessible", true), ExternalVisible = Flag("ExternalVisible", true), ExternalWritable = Flag("ExternalWritable", true),
            Retain = Flag("Retain", false), SetPoint = Flag("SetPoint", false),
        };
    }

    private static string Folder(string? folderPath)
        => "/" + string.Join("/", (folderPath ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries));
}
