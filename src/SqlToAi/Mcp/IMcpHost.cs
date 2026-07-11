#nullable enable

namespace SqlToAi.Mcp;

/// <summary>
/// Defines the host runner for the Model Context Protocol (MCP) server.
/// </summary>
public interface IMcpHost
{
    /// <summary>
    /// Runs the MCP server, listening for client messages on Stdio.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop the server.</param>
    /// <returns>A task representing the server execution life.</returns>
    Task RunAsync(CancellationToken cancellationToken = default);
}
