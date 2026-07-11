#nullable enable

namespace SqlToAi.Mcp;

/// <summary>
/// Defines the host runner for the Model Context Protocol (MCP) server.
/// </summary>
public interface IMcpHost
{
    /// <summary>
    /// Runs the MCP server loop, reading JSON-RPC messages from <paramref name="input"/>
    /// and writing responses to <paramref name="output"/>.
    /// </summary>
    /// <param name="input">Stream to read newline-delimited JSON-RPC messages from.</param>
    /// <param name="output">Stream to write JSON-RPC responses to.</param>
    /// <param name="cancellationToken">Cancellation token to stop the server.</param>
    /// <returns>A task representing the server execution life.</returns>
    Task RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default);
}
