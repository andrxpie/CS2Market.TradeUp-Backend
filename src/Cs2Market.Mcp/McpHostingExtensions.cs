using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Extensions.Hosting;

public static class McpHostingExtensions
{
    /// <summary>
    /// Registers the MCP server and its API client. Called by the standalone MCP host in
    /// development and by the API host when it runs as a single process in production.
    /// </summary>
    public static TBuilder AddMcpSurface<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The ModelContextProtocol server, its tools and the typed API client arrive in M6-1.

        return builder;
    }

    /// <summary>Maps the MCP HTTP transport endpoint.</summary>
    public static IEndpointRouteBuilder MapMcpSurface(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // /mcp is mapped here in M6-1.

        return endpoints;
    }
}
