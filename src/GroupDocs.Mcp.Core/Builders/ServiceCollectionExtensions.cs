using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Builders;
using GroupDocs.Mcp.Core.Diagnostics;
using GroupDocs.Mcp.Core.Licensing;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

public static class GroupDocsMcpServiceCollectionExtensions
{
    public static GroupDocsMcpBuilder AddGroupDocsMcp(
        this IServiceCollection services, Action<McpConfig>? configure = null)
    {
        services
            .AddOptions<McpConfig>()
            .Configure(config =>
            {
                configure?.Invoke(config);
            });

        // Register logging-wrapped FileResolver.
        // FileResolver depends on IFileStorage, which is registered later by .AddLocalStorage() etc.
        services.AddTransient<FileResolver>();
        services.AddTransient<IFileResolver>(sp =>
        {
            var inner = sp.GetRequiredService<FileResolver>();
            var logger = sp.GetRequiredService<ILogger<LoggingFileResolver>>();
            return new LoggingFileResolver(inner, logger);
        });

        // Register OutputHelper
        services.AddTransient<OutputHelper>();

        // Contribute the shared error filter and the get_license_status tool to the MCP server.
        //
        // This runs BEFORE AddMcpServer() in every product's Program.cs and touches only the
        // service collection, which is exactly why it works: Configure<T> callbacks are applied
        // when the options are resolved, not when they are registered. That means no product
        // needs a registration line for either of these — verified end to end against a live
        // stdio server.
        services.AddOptions<McpServerOptions>().Configure<IServiceProvider>((options, sp) =>
        {
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger(typeof(ToolErrorFilter))
                         ?? Logging.Abstractions.NullLogger.Instance;

            options.Filters.Request.CallToolFilters.Add(ToolErrorFilter.Create(logger));

            options.ToolCollection ??= [];
            options.ToolCollection.Add(McpServerTool.Create(
                (ILicenseManager licenseManager) => LicenseStatusTool.Build(licenseManager),
                new McpServerToolCreateOptions
                {
                    Name = LicenseStatusTool.ToolName,
                    Description = LicenseStatusTool.ToolDescription,
                    Services = sp
                }));
        });

        return new GroupDocsMcpBuilder(services);
    }
}
