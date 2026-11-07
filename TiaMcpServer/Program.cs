using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TiaMcpServer.Cli;
using TiaMcpServer.Cli.Install;
using TiaMcpServer.Cursors;
using TiaMcpServer.Hmi;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;

namespace TiaMcpServer
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "install", StringComparison.OrdinalIgnoreCase))
            {
                return await InstallCommand.RunAsync(args[1..]);
            }

            if (args.Length > 0 && string.Equals(args[0], "doctor", StringComparison.OrdinalIgnoreCase))
            {
                return await DoctorCommand.RunAsync(args[1..]);
            }

            if (args.Length > 0 && string.Equals(args[0], "passwords", StringComparison.OrdinalIgnoreCase))
            {
                return PasswordManagerCommand.Run(args[1..]);
            }

            if (args.Length > 0 && (string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(args[0], "-v", StringComparison.OrdinalIgnoreCase)))
            {
                return VersionCommand.Run(Console.Out);
            }

            if (RemovedCliOptions.TryGetError(args, out var removedOptionError))
            {
                Console.Error.WriteLine($"Error: {removedOptionError}");
                return 1;
            }

            // The owner's settings file (written only by the password manager; never by this process).
            var serverSettings = TiaMcpServer.Contracts.Shared.ServerSettingsFile.Read(Environment.GetEnvironmentVariable,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            var accessModeResult = AccessModeParser.Parse(args, Environment.GetEnvironmentVariable, serverSettings);
            if (!accessModeResult.IsValid)
            {
                Console.Error.WriteLine($"Error: {accessModeResult.Error}");
                return 1;
            }

            var accessMode = accessModeResult.Mode;
            var accessPolicy = new OperationAccessPolicy(accessMode);
            if (!TryResolveStartupProjectPath(args, out var startupProjectPath, out var projectPathError))
            {
                Console.Error.WriteLine($"Error: {projectPathError}");
                return 1;
            }

            var budgetResult = PayloadBudgetParser.Parse(args);
            if (!budgetResult.IsValid)
            {
                Console.Error.WriteLine($"Error: {budgetResult.Error}");
                return 1;
            }

            StructuredOperationBatchPayloadBudget.Configure(budgetResult.ItemChars, budgetResult.DocumentChars);

            var workerTimeout = WorkerTimeoutParser.Parse(args);
            if (!workerTimeout.IsValid)
            {
                Console.Error.WriteLine($"Error: {workerTimeout.Error}");
                return 1;
            }

            var (plcWebApiConfiguration, plcWebApiError) = PlcWebApi.PlcWebApiConfigOption.Resolve(args);
            if (plcWebApiConfiguration is null)
            {
                Console.Error.WriteLine($"Error: {plcWebApiError}");
                return 1;
            }

            var (docsOptions, docsError) = SiemensDocs.SiemensDocsOptions.Resolve(args);
            if (docsOptions is null)
            {
                Console.Error.WriteLine($"Error: {docsError}");
                return 1;
            }

            if (TiaMcpServer.Contracts.Shared.PasswordSources.ConfigureCredentialPrefix(Environment.GetEnvironmentVariable) is { } prefixError)
            {
                Console.Error.WriteLine($"Error: {prefixError}");
                return 1;
            }

            if (PlcDownload.PasswordMapStore.Load(Environment.GetEnvironmentVariable,
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)) is { } passwordMapError)
            {
                Console.Error.WriteLine($"Error: {passwordMapError}");
                return 1;
            }

            Console.Error.WriteLine($"TIA MCP access mode: {McpAccessModeNames.ToName(accessMode).ToUpperInvariant()}");
            Console.Error.WriteLine($"TIA MCP access mode source: {accessModeResult.DescribeSource()}");
            Console.Error.WriteLine($"TIA MCP settings file: {serverSettings.Path ?? "none"}");
            Console.Error.WriteLine($"TIA MCP response budget: {budgetResult.ItemChars} characters per value, {budgetResult.DocumentChars} per response");
            Console.Error.WriteLine($"TIA MCP worker timeout: {workerTimeout.Minutes} minutes");
            Console.Error.WriteLine($"TIA MCP PLC Web API targets: {PlcWebApi.PlcWebApiConfigOption.Describe(plcWebApiConfiguration)}");
            Console.Error.WriteLine($"TIA MCP Siemens documentation: {docsOptions.Describe()}");
            Console.Error.WriteLine($"TIA MCP credential prefix: {TiaMcpServer.Contracts.Shared.PasswordSources.DescribeCredentialPrefix()}");
            Console.Error.WriteLine($"TIA MCP password map: {(PlcDownload.PasswordMapStore.LoadedPath is { } map ? $"{map} ({PlcDownload.PasswordMapStore.Entries.Count} entries)" : "none")}");
            if (accessMode == McpAccessMode.ReadOnly)
            {
                Console.Error.WriteLine("Project opening, compilation, writes and lifecycle operations are disabled.");
            }

            // Application-specific access-mode switches have been parsed. Remove them
            // before generic-host configuration processes the remaining command line because
            // value-less switches such as --read-only are not valid configuration key/value pairs.
            var builder = Host.CreateApplicationBuilder(
                HostArgumentFilter.RemoveAccessModeArguments(args));
            builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddSingleton(new ProjectSessionBinding(startupProjectPath));
            builder.Services.AddSingleton(accessPolicy);
            builder.Services.AddSingleton(new Vci.VciBreadcrumbLocation());
            builder.Services.AddSingleton(new ClaudeInbox.ClaudeInboxLocation());
            builder.Services.AddSingleton(sp => new OpennessWorkerClient(
                sp.GetRequiredService<ProjectSessionBinding>(),
                sp.GetRequiredService<ILogger<OpennessWorkerClient>>(),
                requestTimeout: workerTimeout.Timeout,
                accessPolicy: sp.GetRequiredService<OperationAccessPolicy>()));
            builder.Services.AddSingleton(sp => new PlcSim.PlcSimWorkerClient(
                sp.GetRequiredService<OperationAccessPolicy>(),
                requestTimeout: workerTimeout.Timeout,
                logger: sp.GetRequiredService<ILogger<PlcSim.PlcSimWorkerClient>>()));
            builder.Services.AddSingleton(_ => new PlcSim.PlcSimAudit(new JsonlWriteAuditSink(), TimeProvider.System));
            // Online S7-1500 Web API sessions live in the host; DI disposal logs authenticated sessions out on shutdown.
            builder.Services.AddSingleton(sp => new PlcWebApi.PlcWebApiService(plcWebApiConfiguration,
                sp.GetRequiredService<OperationAccessPolicy>(), new PlcWebApi.SiemensPlcWebApiClientFactory(), new JsonlWriteAuditSink()));
            // siemens_docs reads the allowed Siemens documentation hosts from the host process (no worker, no TIA Portal).
            builder.Services.AddSingleton(sp => new SiemensDocs.SiemensDocsService(docsOptions, sp.GetRequiredService<OperationAccessPolicy>()));
            builder.Services.AddSingleton(sp => new WriteExecution(
                new OpennessWriteBindingGate(sp.GetRequiredService<OpennessWorkerClient>()),
                new JsonlWriteAuditSink(), WriteGuardRegistration.ProductionCatalog(), TimeProvider.System));
            builder.Services.AddSingleton(_ => AuthenticatedCursorProtector.CreateProcessScoped());
            builder.Services.AddSingleton(sp => new HardwarePageCursorCodec(
                sp.GetRequiredService<AuthenticatedCursorProtector>()));
            builder.Services.AddSingleton(sp => new NetworkObjectPageCursorCodec(
                sp.GetRequiredService<AuthenticatedCursorProtector>()));
            builder.Services.AddSingleton(sp => new NetworkObjectPaginationCoordinator(
                sp.GetRequiredService<OpennessWorkerClient>(),
                sp.GetRequiredService<NetworkObjectPageCursorCodec>()));
            builder.Services.AddSingleton(sp => new HardwarePageProjector(
                sp.GetRequiredService<HardwarePageCursorCodec>()));
            builder.Services.AddSingleton(sp => new HardwarePaginationCoordinator(
                sp.GetRequiredService<OpennessWorkerClient>(),
                sp.GetRequiredService<HardwarePageCursorCodec>(),
                sp.GetRequiredService<HardwarePageProjector>()));
            builder.Services.AddSingleton(sp => new ProjectTreeCursorCodec(
                sp.GetRequiredService<AuthenticatedCursorProtector>()));
            builder.Services.AddSingleton(sp => new HmiPageCursorCodec(
                sp.GetRequiredService<AuthenticatedCursorProtector>()));
            builder.Services.AddSingleton(sp => new ProjectTreeSnapshotStore(TimeProvider.System));
            builder.Services.AddSingleton(sp => new ProjectTreePageProjector(
                sp.GetRequiredService<ProjectTreeCursorCodec>()));
            builder.Services.AddSingleton(sp => new ProjectTreeBrowseCoordinator(
                sp.GetRequiredService<OpennessWorkerClient>(),
                sp.GetRequiredService<ProjectTreeCursorCodec>(),
                sp.GetRequiredService<ProjectTreeSnapshotStore>(),
                sp.GetRequiredService<ProjectTreePageProjector>(),
                TimeProvider.System));
            builder.Services.AddSingleton(sp => new NetworkReadOperationExecutor(
                sp.GetRequiredService<OpennessWorkerClient>(),
                sp.GetRequiredService<HardwarePaginationCoordinator>(),
                sp.GetRequiredService<NetworkObjectPaginationCoordinator>()));

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithAccessModeTools(accessMode);

            using var host = builder.Build();
            await host.RunAsync();
            return 0;
        }

        private static bool TryResolveStartupProjectPath(
            string[] args,
            out string? projectPath,
            out string? error)
        {
            projectPath = null;
            error = null;
            var source = "--project";
            for (int i = 0; i < args.Length; i++)
            {
                string? candidate = null;
                if (string.Equals(args[i], "--project", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length ||
                        string.IsNullOrWhiteSpace(args[i + 1]) ||
                        args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        error = "--project requires a value.";
                        return false;
                    }

                    candidate = args[++i];
                }
                else
                {
                    const string projectPrefix = "--project=";
                    if (args[i].StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        candidate = args[i].Substring(projectPrefix.Length);
                        if (string.IsNullOrWhiteSpace(candidate))
                        {
                            error = "--project requires a value.";
                            return false;
                        }
                    }
                }

                if (candidate is null)
                {
                    continue;
                }

                if (projectPath is not null)
                {
                    error = "--project may be specified only once.";
                    return false;
                }

                projectPath = candidate;
            }

            if (projectPath is null)
            {
                source = ConfiguredProjectPath.EnvVar;
                projectPath = Environment.GetEnvironmentVariable(ConfiguredProjectPath.EnvVar);
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    projectPath = null;
                    return true;
                }
            }

            var invalid = ConfiguredProjectPath.Validate(projectPath.Trim());
            if (invalid is not null)
            {
                error = $"{source} {invalid} Got '{projectPath}'.";
                return false;
            }

            return true;
        }
    }
}
