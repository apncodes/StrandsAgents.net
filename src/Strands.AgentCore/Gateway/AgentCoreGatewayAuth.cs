namespace Strands.AgentCore;

/// <summary>
/// Inbound authorization configuration for an AgentCore Gateway endpoint.
/// Choose the mode that matches your gateway's inbound authorization setting.
/// </summary>
public abstract record AgentCoreGatewayAuth
{
    private AgentCoreGatewayAuth() { }

    /// <summary>
    /// JWT Bearer token auth. Use when the gateway is configured with JWT/OIDC inbound
    /// authorization (Amazon Cognito, Entra ID, Okta, Google, GitHub, etc.).
    /// The token is sent as <c>Authorization: Bearer {token}</c>.
    /// </summary>
    public sealed record Bearer(string AccessToken) : AgentCoreGatewayAuth;

    /// <summary>
    /// IAM SigV4 auth. Use when the gateway is configured with IAM inbound authorization.
    /// Credentials are resolved from the standard AWS credential chain
    /// (env vars → ~/.aws/credentials → instance metadata) — same source as BedrockModel.
    /// Requires <c>bedrock-agentcore:InvokeGateway</c> permission on the gateway ARN.
    /// </summary>
    public sealed record Iam(string Region = "us-east-1") : AgentCoreGatewayAuth;

    /// <summary>
    /// No application-level auth. Use when the gateway is configured with no inbound
    /// authorization and access is controlled at the network level (security groups, VPC
    /// peering, subnet routing). Typical for peripheral agents running inside AgentCore
    /// Runtime that call a gateway in the same or adjacent subnet.
    /// </summary>
    public sealed record None : AgentCoreGatewayAuth;
}
