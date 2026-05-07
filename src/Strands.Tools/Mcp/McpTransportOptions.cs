namespace Strands.Tools.Mcp;

/// <summary>Base type for MCP transport configuration options.</summary>
public abstract record McpTransportOptions;

/// <summary>Transport options for launching an MCP server as a local subprocess over stdio.</summary>
/// <param name="Command">The executable to launch (e.g. "npx", "python").</param>
/// <param name="Args">Arguments passed to the command.</param>
public record StdioTransportOptions(string Command, string[] Args) : McpTransportOptions;

/// <summary>Transport options for connecting to a remote MCP server over Server-Sent Events.</summary>
/// <param name="Endpoint">The SSE endpoint URI of the MCP server.</param>
public record SseTransportOptions(Uri Endpoint) : McpTransportOptions;

/// <summary>Transport options for connecting to an Amazon Bedrock AgentCore Gateway over Streamable HTTP.</summary>
/// <param name="GatewayUrl">The AgentCore Gateway MCP endpoint URL.</param>
public record AgentCoreGatewayTransportOptions(Uri GatewayUrl) : McpTransportOptions;
