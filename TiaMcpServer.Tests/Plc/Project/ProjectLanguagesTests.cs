using System.Collections;
using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Project
{
    /// <summary>
    /// Issue #8: project languages and project texts. The Siemens-free run against stand-ins (members as Siemens.Engineering.Base.xml
    /// documents them), the host catalog, policy and decoding, and the plc-write-roundtrip FakeWorker end to end. Not live-tested:
    /// whether the editing/reference language is writable, the workbook layout and what an import changes are decided only live.
    /// </summary>
    [Collection("Mcp protocol serial")]
    public sealed class ProjectLanguagesTests
    {
        private sealed class TextLangDouble(string culture)
        {
            public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(culture);
        }

        private sealed class TextCompositionDouble(List<TextLangDouble> items) : IEnumerable<TextLangDouble>
        {
            public TextLangDouble? Find(CultureInfo culture) => items.FirstOrDefault(l => l.Culture.Name == culture.Name);

            public IEnumerator<TextLangDouble> GetEnumerator() => items.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class TextAssociationDouble : IEnumerable<TextLangDouble>
        {
            public List<TextLangDouble> Items { get; } = new();

            public bool Silent { get; set; }

            public void Add(TextLangDouble language)
            {
                if (!Silent) Items.Add(language);
            }

            public bool Remove(TextLangDouble language) => Items.Remove(language);

            public IEnumerator<TextLangDouble> GetEnumerator() => Items.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class TextSettingsDouble
        {
            public TextCompositionDouble Languages { get; init; } = null!;

            public TextAssociationDouble ActiveLanguages { get; } = new();

            public TextLangDouble EditingLanguage { get; set; } = null!;

            public TextLangDouble ReferenceLanguage { get; set; } = null!;
        }

        private sealed class TextReadOnlySettingsDouble(TextCompositionDouble languages, TextLangDouble editing)
        {
            public TextCompositionDouble Languages { get; } = languages;

            public TextAssociationDouble ActiveLanguages { get; } = new();

            public TextLangDouble EditingLanguage { get; } = editing;

            public TextLangDouble ReferenceLanguage => EditingLanguage;
        }

        private sealed class TextProjectDouble(object settings)
        {
            public object LanguageSettings { get; } = settings;

            public List<Siemens.Engineering.TextCategory> TextCategories { get; } = new() { new("Comment", "Kommentar") };

            public List<string> Exports { get; } = new();

            public void ExportProjectTexts(FileInfo path, CultureInfo source, CultureInfo target)
            {
                Exports.Add($"cultures {source.Name}->{target.Name}");
                File.WriteAllText(path.FullName, "x");
            }
        }

        private static (TextProjectDouble TextProjectDouble, TextSettingsDouble TextSettingsDouble) Fixture()
        {
            var supported = new[] { "de-DE", "en-US", "fr-FR" }.Select(c => new TextLangDouble(c)).ToList();
            var settings = new TextSettingsDouble { Languages = new TextCompositionDouble(supported), EditingLanguage = supported[0], ReferenceLanguage = supported[0] };
            settings.ActiveLanguages.Items.AddRange(supported.Take(2));
            return (new TextProjectDouble(settings), settings);
        }

        private static ProjectLanguageTarget Target(object project)
            => new(project, _ => Array.Empty<(string, object)>());

        [Fact]
        public void Read_And_Set_AddRemoveAndSetLanguages_ReadBack()
        {
            var (project, settings) = Fixture();
            var read = ProjectLanguagesRun.Read(Target(project), _ => false);
            Assert.Equal(new[] { "de-DE", "en-US" }, read.State.Active);
            Assert.Equal(("de-DE", "de-DE"), (read.State.Editing, read.State.Reference));
            Assert.Equal(new[] { "de-DE", "en-US", "fr-FR" }, read.Supported);
            Assert.Equal("Comment", Assert.Single(read.TextCategories).Identifier);

            var result = ProjectLanguagesRun.Set(Target(project), new ProjectLanguagesRequestInfo
            {
                AddLanguages = { "fr-FR" }, RemoveLanguages = { "de-DE" }, EditingLanguage = "en-US", ReferenceLanguage = "fr-FR",
            }, _ => false);
            Assert.Equal(new[] { "en-US", "fr-FR" }, result.After.Active);
            Assert.Equal(("en-US", "fr-FR"), (result.After.Editing, result.After.Reference));
            Assert.Equal(new[] { "de-DE", "en-US" }, result.Before.Active);
            Assert.Equal(4, result.Calls.Count);
            Assert.Equal("en-US", settings.EditingLanguage.Culture.Name);
        }

        [Fact]
        public void Set_RefusesUnknownCultures_ReadOnlySetters_AndAnIgnoredAdd()
        {
            var (project, settings) = Fixture();
            var unknown = Assert.Throws<RuntimeBindingException>(() =>
                ProjectLanguagesRun.Set(Target(project), new ProjectLanguagesRequestInfo { AddLanguages = { "it-IT" }, EditingLanguage = "en-US" }, _ => false));
            Assert.Equal(WorkerFailureCategories.TargetNotFound, unknown.Category);
            Assert.Equal("de-DE", settings.EditingLanguage.Culture.Name);

            var supported = new[] { "de-DE", "en-US" }.Select(c => new TextLangDouble(c)).ToList();
            var readOnly = new TextProjectDouble(new TextReadOnlySettingsDouble(new TextCompositionDouble(supported), supported[0]));
            var missing = Assert.Throws<RuntimeBindingException>(() =>
                ProjectLanguagesRun.Set(Target(readOnly), new ProjectLanguagesRequestInfo { EditingLanguage = "en-US" }, _ => false));
            Assert.Equal(WorkerFailureCategories.ApiMemberMissing, missing.Category);

            settings.ActiveLanguages.Silent = true;
            var ignored = Assert.Throws<RuntimeBindingException>(() =>
                ProjectLanguagesRun.Set(Target(project), new ProjectLanguagesRequestInfo { AddLanguages = { "fr-FR" } }, _ => false));
            Assert.Equal(WorkerFailureCategories.PostconditionFailed, ignored.Category);
        }

        [Fact]
        public void Export_CultureOverload_PlacesTheFile_AndTheFilteredOverloadIsMissingHere()
        {
            var directory = Directory.CreateTempSubdirectory("tia-mcp-texts-").FullName;
            try
            {
                var (project, _) = Fixture();
                var path = Path.Combine(directory, "texts.xlsx");
                var result = ProjectLanguagesRun.Export(Target(project), new ProjectLanguagesRequestInfo
                {
                    FilePath = path, SourceLanguage = "de-DE", TargetLanguages = { "en-US" },
                }, DateTime.UtcNow, _ => false);
                Assert.Equal(ProjectLanguageNames.OverloadCultures, result.Overload);
                Assert.Equal("cultures de-DE->en-US", Assert.Single(project.Exports));
                Assert.Equal(TiaMcpServer.Contracts.PlcAlarms.PlcAlarmFiles.Hash(path).Sha256, result.Sha256);

                var exists = Assert.Throws<RuntimeBindingException>(() => ProjectLanguagesRun.Export(Target(project), new ProjectLanguagesRequestInfo
                {
                    FilePath = path, SourceLanguage = "de-DE", TargetLanguages = { "en-US" },
                }, DateTime.UtcNow, _ => false));
                Assert.Equal(WorkerFailureCategories.ValidationError, exists.Category);

                var filtered = Assert.Throws<RuntimeBindingException>(() => ProjectLanguagesRun.Export(Target(project), new ProjectLanguagesRequestInfo
                {
                    FilePath = Path.Combine(directory, "two.xlsx"), SourceLanguage = "de-DE", TargetLanguages = { "en-US", "fr-FR" },
                }, DateTime.UtcNow, _ => false));
                Assert.Equal(WorkerFailureCategories.ApiMemberMissing, filtered.Category);
                Assert.False(File.Exists(Path.Combine(directory, "two.xlsx")));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Catalog_Policy_AndDecoding()
        {
            PlcOperationRequest Set(Action<PlcOperationRequest> configure)
            {
                var op = new PlcOperationRequest { OperationId = "s", Operation = ProjectLanguageMethods.SetLanguages };
                configure(op);
                return op;
            }

            Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Set(o => o.AddLanguages = new[] { "fr-FR" }) }).IsValid);
            Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Set(_ => { }) }).IsValid);
            Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Set(o => o.AddLanguages = new[] { "not a culture" }) }).IsValid);
            Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Set(o => { o.AddLanguages = new[] { "fr-FR" }; o.RemoveLanguages = new[] { "FR-fr" }; }) }).IsValid);
            Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Set(o => { o.EditingLanguage = "en-US"; o.PlcName = "PLC_1"; }) }).IsValid);

            var export = new PlcOperationRequest
            {
                OperationId = "e", Operation = ProjectLanguageMethods.ExportTexts, FilePath = OperatingSystem.IsWindows() ? @"C:\t\texts.xlsx" : "/tmp/texts.xlsx",
                SourceLanguage = "de-DE", TargetLanguages = new[] { "en-US" },
            };
            var exportCheck = PlcOperationCatalog.ValidateWrite(new[] { export });
            Assert.True(exportCheck.IsValid, exportCheck.Error);
            export.TargetLanguages = null;
            Assert.False(PlcOperationCatalog.ValidateWrite(new[] { export }).IsValid);
            export.TargetLanguages = new[] { "en-US" };
            export.FilePath = export.FilePath!.Replace(".xlsx", ".csv");
            Assert.False(PlcOperationCatalog.ValidateWrite(new[] { export }).IsValid);
            Assert.True(PlcOperationCatalog.ValidateRead(new[] { new PlcOperationRequest { OperationId = "r", Operation = ProjectLanguageMethods.GetLanguages } }).IsValid);

            Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(ProjectLanguageMethods.GetLanguages));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, ProjectLanguageMethods.GetLanguages));
            foreach (var method in ProjectLanguageMethods.Write)
            {
                Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
                Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
                Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(method));
                // ProjectBase members: local sessions too, like every other plc_write content operation.
                Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method));
            }

            var item = Set(o => o.AddLanguages = new[] { "fr-FR" });
            var good = new ProjectLanguagesWriteResultInfo
            {
                Before = new ProjectLanguageStateInfo { Active = { "de-DE" }, Editing = "de-DE", Reference = "de-DE" },
                After = new ProjectLanguageStateInfo { Active = { "de-DE", "fr-FR" }, Editing = "de-DE", Reference = "de-DE" },
                Added = { "fr-FR" }, Calls = { "ActiveLanguages.Add(fr-FR)" },
            };
            var results = new Dictionary<string, JsonElement> { ["s"] = PlcProjectLanguages.DecodeWrite(item, WorkerJson.SerializePayload(good)) };
            Assert.True(PlcProjectLanguages.HasReadBack(item, results));
            var check = PlcProjectLanguages.Verify(item, results);
            Assert.Equal((PlcProjectLanguages.LanguagesCheck, true), (check.Check, check.Success));
            good.Added.Clear();
            Assert.ThrowsAny<JsonException>(() => PlcProjectLanguages.DecodeWrite(item, WorkerJson.SerializePayload(good)));
        }

        [Fact]
        public async Task Mcp_SetsLanguages_ExportsAndImportsProjectTexts_Verified()
        {
            var directory = Directory.CreateTempSubdirectory("tia-mcp-texts-mcp-").FullName;
            var audit = new TempAuditDirectory();
            Directory.CreateDirectory(audit.Path);
            var ui = new FakeWorkerUiOpenProject("plc-write-roundtrip");
            var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "plc-write-roundtrip");
            try
            {
                async Task<JsonElement> Call(string tool, params object[] operations)
                {
                    var raw = await harness.Client.CallToolAsync(tool, new Dictionary<string, object?> { ["operations"] = operations });
                    Assert.Empty(StructuredContractInspector.FindViolations(raw));
                    return raw.StructuredContent!.Value;
                }

                var read = (await Call("plc_read", new { operationId = "r", operation = "get_project_languages" })).GetProperty("batch").GetProperty("operations")[0];
                Assert.True(read.GetProperty("status").GetString() == "succeeded", read.ToString());
                Assert.Equal(2, read.GetProperty("result").GetProperty("state").GetProperty("active").GetArrayLength());

                var preview = await Call("plc_write", new { operationId = "s", operation = "set_project_languages", addLanguages = new[] { "fr-FR" } });
                Assert.Contains(preview.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == PlcGuardDefinitions.ChangesProjectLanguages);
                Assert.True(preview.GetProperty("success").GetBoolean(), preview.ToString());
                Assert.Equal("projectLanguages", preview.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());

                var unknown = await Call("plc_write", new { operationId = "u", operation = "set_project_languages", addLanguages = new[] { "ja-JP" } });
                Assert.Equal(WorkerFailureCategories.TargetNotFound,
                    unknown.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("category").GetString());

                var file = Path.Combine(directory, "texts.xlsx");
                var exported = await Call("plc_write", new
                {
                    operationId = "e", operation = "export_project_texts", filePath = file, sourceLanguage = "de-DE", targetLanguages = new[] { "en-US", "fr-FR" },
                });
                Assert.True(exported.GetProperty("success").GetBoolean(), exported.ToString());
                var exportResult = exported.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
                Assert.Equal(ProjectLanguageNames.OverloadFiltered, exportResult.GetProperty("overload").GetString());
                Assert.Equal(2, exportResult.GetProperty("devices").GetArrayLength());
                Assert.True(File.Exists(file));

                var imported = await Call("plc_write", new { operationId = "i", operation = "import_project_texts", filePath = file });
                Assert.True(imported.GetProperty("success").GetBoolean(), imported.ToString());
                Assert.Contains(imported.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == PlcGuardDefinitions.ImportsProjectTexts);
                var importCheck = imported.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("importCheck");
                Assert.True(importCheck.GetProperty("outcome").GetString() == ProjectLanguageNames.Verified, importCheck.ToString());
                Assert.Equal(PlcProjectLanguages.ImportedRowsCheck, imported.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());

                var badDevice = await Call("plc_write", new
                {
                    operationId = "d", operation = "export_project_texts", filePath = Path.Combine(directory, "dev.xlsx"), sourceLanguage = "de-DE",
                    targetLanguages = new[] { "en-US" }, deviceNames = new[] { "NoSuchDevice" },
                });
                Assert.Equal(WorkerFailureCategories.TargetNotFound,
                    badDevice.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("category").GetString());
            }
            finally
            {
                await harness.DisposeAsync();
                ui.Dispose();
                audit.Dispose();
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
