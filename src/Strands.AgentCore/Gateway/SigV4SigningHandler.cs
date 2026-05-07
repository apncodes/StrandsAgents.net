using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;

namespace Strands.AgentCore;

/// <summary>
/// A <see cref="DelegatingHandler"/> that signs outgoing HTTP requests with AWS SigV4.
/// Used only by the IAM auth path in <see cref="AgentCoreGatewayToolProvider"/>.
/// </summary>
internal sealed class SigV4SigningHandler : DelegatingHandler
{
    private readonly AWSCredentials _credentials;
    private readonly string _region;
    private readonly string _serviceName;

    /// <summary>
    /// Initializes a new instance of <see cref="SigV4SigningHandler"/>.
    /// </summary>
    /// <param name="credentials">AWS credentials (supports rotation via <c>GetCredentialsAsync</c>).</param>
    /// <param name="region">AWS region (e.g. <c>us-east-1</c>).</param>
    /// <param name="serviceName">AWS service name for signing (e.g. <c>bedrock-agentcore</c>).</param>
    public SigV4SigningHandler(AWSCredentials credentials, string region, string serviceName)
    {
        _credentials = credentials;
        _region = region;
        _serviceName = serviceName;
        InnerHandler = new HttpClientHandler();
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        // Resolve credentials fresh on each request to respect rotation.
        ImmutableCredentials creds = await _credentials.GetCredentialsAsync().ConfigureAwait(false);

        // Timestamp in SigV4 format.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string timestamp = now.ToString("yyyyMMddTHHmmssZ");
        string date = now.ToString("yyyyMMdd");

        // Read body bytes for hashing (may be null for GET/DELETE).
        byte[] bodyBytes = request.Content is not null
            ? await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false)
            : [];

        string bodyHash = HexHash(bodyBytes);

        // Set required headers before building the canonical request.
        request.Headers.Remove("X-Amz-Date");
        request.Headers.TryAddWithoutValidation("X-Amz-Date", timestamp);

        if (!string.IsNullOrEmpty(creds.Token))
        {
            request.Headers.Remove("X-Amz-Security-Token");
            request.Headers.TryAddWithoutValidation("X-Amz-Security-Token", creds.Token);
        }

        // Derive host from the request URI.
        string host = request.RequestUri!.Host;
        if (!request.RequestUri.IsDefaultPort)
            host += ":" + request.RequestUri.Port;

        // Ensure Host header is set (required for canonical request).
        request.Headers.Host = host;

        // Collect signed headers (must be sorted, lowercase).
        // We sign: content-type, host, x-amz-date (and x-amz-security-token when present).
        string contentType = request.Content?.Headers.ContentType?.ToString() ?? string.Empty;

        var signedHeaderNames = new List<string> { "host", "x-amz-date" };
        if (!string.IsNullOrEmpty(contentType))
            signedHeaderNames.Insert(0, "content-type");
        if (!string.IsNullOrEmpty(creds.Token))
            signedHeaderNames.Add("x-amz-security-token");

        signedHeaderNames.Sort(StringComparer.Ordinal);
        string signedHeaders = string.Join(";", signedHeaderNames);

        // Build canonical headers string (each header: lowercase-name + ":" + trimmed-value + "\n").
        var canonicalHeadersBuilder = new StringBuilder();
        foreach (string headerName in signedHeaderNames)
        {
            string value = headerName switch
            {
                "content-type" => contentType,
                "host" => host,
                "x-amz-date" => timestamp,
                "x-amz-security-token" => creds.Token!,
                _ => string.Empty
            };
            canonicalHeadersBuilder.Append(headerName).Append(':').Append(value.Trim()).Append('\n');
        }
        string canonicalHeaders = canonicalHeadersBuilder.ToString();

        // Build canonical URI (path, percent-encoded).
        string canonicalUri = string.IsNullOrEmpty(request.RequestUri.AbsolutePath)
            ? "/"
            : request.RequestUri.AbsolutePath;

        // Build canonical query string (sorted by key).
        string canonicalQueryString = BuildCanonicalQueryString(request.RequestUri.Query);

        // Canonical request.
        string canonicalRequest =
            request.Method.Method + "\n" +
            canonicalUri + "\n" +
            canonicalQueryString + "\n" +
            canonicalHeaders + "\n" +
            signedHeaders + "\n" +
            bodyHash;

        string canonicalRequestHash = HexHash(Encoding.UTF8.GetBytes(canonicalRequest));

        // Credential scope.
        string credentialScope = $"{date}/{_region}/{_serviceName}/aws4_request";

        // String to sign.
        string stringToSign =
            "AWS4-HMAC-SHA256\n" +
            timestamp + "\n" +
            credentialScope + "\n" +
            canonicalRequestHash;

        // Derive signing key via HMAC chain.
        byte[] signingKey = DeriveSigningKey(creds.SecretKey, date, _region, _serviceName);

        // Compute signature.
        string signature = HexHmac(signingKey, stringToSign);

        // Set Authorization header.
        string authorization =
            $"AWS4-HMAC-SHA256 Credential={creds.AccessKey}/{credentialScope}, " +
            $"SignedHeaders={signedHeaders}, " +
            $"Signature={signature}";

        request.Headers.Remove("Authorization");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        return await base.SendAsync(request, ct).ConfigureAwait(false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string HexHash(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    private static string HexHmac(byte[] key, string data)
    {
        byte[] mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
        return Convert.ToHexStringLower(mac);
    }

    private static byte[] HmacBytes(byte[] key, string data)
        => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    /// <summary>
    /// Derives the SigV4 signing key via the standard HMAC chain:
    /// HMAC(HMAC(HMAC(HMAC("AWS4" + secretKey, date), region), service), "aws4_request")
    /// </summary>
    private static byte[] DeriveSigningKey(string secretKey, string date, string region, string service)
    {
        byte[] kDate    = HmacBytes(Encoding.UTF8.GetBytes("AWS4" + secretKey), date);
        byte[] kRegion  = HmacBytes(kDate, region);
        byte[] kService = HmacBytes(kRegion, service);
        byte[] kSigning = HmacBytes(kService, "aws4_request");
        return kSigning;
    }

    /// <summary>
    /// Builds a canonical query string from a raw URI query component.
    /// Keys and values are URI-encoded and sorted lexicographically by key.
    /// </summary>
    private static string BuildCanonicalQueryString(string rawQuery)
    {
        if (string.IsNullOrEmpty(rawQuery))
            return string.Empty;

        // Strip leading '?'
        string query = rawQuery.TrimStart('?');
        if (string.IsNullOrEmpty(query))
            return string.Empty;

        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p =>
            {
                int eq = p.IndexOf('=');
                if (eq < 0)
                    return (Uri.EscapeDataString(p), string.Empty);
                string k = Uri.EscapeDataString(Uri.UnescapeDataString(p[..eq]));
                string v = Uri.EscapeDataString(Uri.UnescapeDataString(p[(eq + 1)..]));
                return (k, v);
            })
            .OrderBy(p => p.Item1, StringComparer.Ordinal)
            .Select(p => p.Item1 + "=" + p.Item2);

        return string.Join("&", pairs);
    }
}
