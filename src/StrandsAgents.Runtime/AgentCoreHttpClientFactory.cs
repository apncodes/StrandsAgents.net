using Amazon.Runtime;

namespace StrandsAgents.Runtime;

/// <summary>
/// Internal factory for creating <see cref="HttpClient"/> instances that target the
/// Amazon Bedrock AgentCore REST API.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CreateSigned"/> produces a client whose every outgoing request is signed
/// with AWS SigV4 using credentials resolved from the standard AWS credential chain
/// (environment variables, <c>~/.aws/credentials</c>, instance metadata, etc.).
/// </para>
/// <para>
/// This factory is used by <see cref="Session.AgentCoreSessionManager"/>,
/// <see cref="Tools.AgentCoreMemoryTool"/>, and <see cref="Tools.SemanticMemoryTool"/>
/// when no <c>clientOverride</c> is provided. The <c>clientOverride</c> path (for unit
/// tests) bypasses this factory entirely.
/// </para>
/// </remarks>
internal static class AgentCoreHttpClientFactory
{
    /// <summary>
    /// Creates an <see cref="HttpClient"/> with a <see cref="SigV4SigningHandler"/> in its
    /// handler chain, targeting <c>https://bedrock-agentcore.{region}.amazonaws.com</c>.
    /// </summary>
    /// <param name="region">AWS region (e.g. <c>us-east-1</c>).</param>
    /// <returns>A configured, SigV4-signed <see cref="HttpClient"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the AWS credential chain returns no credentials.
    /// </exception>
    public static HttpClient CreateSigned(string region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        AWSCredentials credentials;
        try
        {
            credentials = FallbackCredentialsFactory.GetCredentials();
        }
        catch (AmazonClientException ex)
        {
            throw new InvalidOperationException(
                "Failed to resolve AWS credentials from the credential chain. " +
                "Ensure that valid credentials are configured (environment variables, " +
                "~/.aws/credentials, or instance metadata).",
                ex);
        }

        var signingHandler = new SigV4SigningHandler(credentials, region, "bedrock-agentcore");
        return new HttpClient(signingHandler)
        {
            BaseAddress = new Uri($"https://bedrock-agentcore.{region}.amazonaws.com"),
        };
    }
}
