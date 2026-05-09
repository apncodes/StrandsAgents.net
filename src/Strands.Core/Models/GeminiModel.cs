using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Strands.Core;

/// <summary>
/// Google Gemini model provider using the Gemini Developer REST API.
/// No third-party SDK required — uses HttpClient + System.Text.Json,
/// matching the pattern of <see cref="AnthropicModel"/> and <see cref="OpenAICompatibleModel"/>.
/// </summary>
public sealed class GeminiModel : IModel, IDisposable
{
    /// <summary>Named <see cref="HttpClient"/> key used when registering via <see cref="System.Net.Http.IHttpClientFactory"/>.</summary>
    public const string HttpClientName = "Gemini";

    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private readonly HttpClient _http;
    private readonly string _modelId;
    private readonly string _apiKey;
    private readonly bool _ownsClient;

    /// <summary>
    /// Initializes a new <see cref="GeminiModel"/>.
    /// </summary>
    /// <param name="apiKey">Google AI Studio API key.</param>
    /// <param name="modelId">Gemini model identifier. Default: "gemini-2.0-flash".</param>
    /// <param name="httpClientFactory">
    /// Factory used to create the <see cref="HttpClient"/>. When provided, the client is not
    /// disposed by this instance. Preferred in hosted applications to avoid socket exhaustion.
    /// </param>
    /// <param name="httpClient">
    /// An existing <see cref="HttpClient"/> to reuse. When provided, the client is not disposed
    /// by this instance. Useful for testing.
    /// </param>
    public GeminiModel(
        string apiKey,
        string modelId = "gemini-2.0-flash",
        IHttpClientFactory? httpClientFactory = null,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        _apiKey = apiKey;
        _modelId = modelId;

        if (httpClientFactory is not null)
        {
            _http = httpClientFactory.CreateClient(HttpClientName);
            _ownsClient = false;
        }
        else if (httpClient is not null)
        {
            _http = httpClient;
            _ownsClient = false;
        }
        else
        {
            _http = new HttpClient();
            _ownsClient = true;
        }
    }

    /// <inheritdoc/>
    public async Task<ModelResponse> InvokeAsync(ModelRequest request, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/{_modelId}:generateContent?key={_apiKey}";
        var body = BuildRequestBody(request);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ModelException(
                $"HTTP request to Gemini API failed: {ex.Message}",
                request,
                httpStatusCode: (int?)ex.StatusCode,
                inner: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new ModelException(
                    $"Gemini API returned {(int)response.StatusCode}: {errorBody}",
                    request,
                    httpStatusCode: (int)response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseResponse(json, request);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/{_modelId}:streamGenerateContent?alt=sse&key={_apiKey}";
        var body = BuildRequestBody(request);

        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(reqMsg, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ModelException(
                $"HTTP request to Gemini API failed: {ex.Message}",
                request,
                httpStatusCode: (int?)ex.StatusCode,
                inner: ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new ModelException(
                $"Gemini API returned {statusCode} on stream request.",
                request,
                httpStatusCode: statusCode);
        }

        using var responseOwner = response;
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        string? textContent = null;
        var toolCalls = new List<ToolCall>();
        var usage = TokenUsage.Zero;
        var stopReason = StopReason.EndTurn;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];

            JsonNode? node;
            try { node = JsonNode.Parse(data); } catch { continue; }
            if (node is null) continue;

            var candidates = node["candidates"]?.AsArray();
            if (candidates is null || candidates.Count == 0) continue;

            var candidate = candidates[0];
            var parts = candidate?["content"]?["parts"]?.AsArray();

            if (parts is not null)
            {
                foreach (var part in parts)
                {
                    if (part is null) continue;

                    var textNode = part["text"];
                    if (textNode is not null)
                    {
                        var delta = textNode.GetValue<string>();
                        textContent = (textContent ?? "") + delta;
                        yield return new TextDeltaModelEvent(delta);
                        continue;
                    }

                    var funcCall = part["functionCall"];
                    if (funcCall is not null)
                    {
                        var name = funcCall["name"]?.GetValue<string>() ?? "";
                        var argsNode = funcCall["args"];
                        var argsJson = argsNode?.ToJsonString() ?? "{}";
                        // Gemini doesn't assign tool call IDs — generate a stable one
                        var id = $"call_{Guid.NewGuid():N}";
                        yield return new ToolCallStartModelEvent(id, name);
                        yield return new ToolCallInputDeltaModelEvent(id, argsJson);
                        toolCalls.Add(new ToolCall(id, name, JsonDocument.Parse(argsJson).RootElement));
                    }
                }
            }

            var finishReason = candidate?["finishReason"]?.GetValue<string>();
            if (finishReason is not null)
                stopReason = MapStopReason(finishReason);

            var usageMeta = node["usageMetadata"];
            if (usageMeta is not null)
            {
                var input = usageMeta["promptTokenCount"]?.GetValue<int>() ?? 0;
                var output = usageMeta["candidatesTokenCount"]?.GetValue<int>() ?? 0;
                usage = new TokenUsage(input, output);
            }
        }

        yield return new ModelCompleteEvent(new ModelResponse(textContent, toolCalls, stopReason, usage));
    }

    private string BuildRequestBody(ModelRequest request)
    {
        var contents = new JsonArray();

        foreach (var msg in request.Messages)
        {
            var parts = new JsonArray();

            foreach (var block in msg.Content)
            {
                switch (block)
                {
                    case TextBlock t:
                        parts.Add(new JsonObject { ["text"] = t.Text });
                        break;

                    case ToolUseBlock tu:
                        parts.Add(new JsonObject
                        {
                            ["functionCall"] = new JsonObject
                            {
                                ["name"] = tu.Name,
                                ["args"] = JsonNode.Parse(tu.Input.GetRawText())
                            }
                        });
                        break;

                    case ToolResultBlock tr:
                        // Gemini requires the tool name in functionResponse — resolve it from history
                        var toolName = FindToolName(request.Messages, tr.ToolUseId);
                        parts.Add(new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["name"] = toolName,
                                ["response"] = new JsonObject { ["content"] = tr.Content }
                            }
                        });
                        break;
                }
            }

            var role = msg.Role == Role.User ? "user" : "model";
            contents.Add(new JsonObject
            {
                ["role"] = role,
                ["parts"] = parts
            });
        }

        var obj = new JsonObject { ["contents"] = contents };

        if (request.SystemPrompt is not null)
        {
            obj["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = request.SystemPrompt } }
            };
        }

        if (request.Tools.Count > 0)
        {
            var declarations = new JsonArray();
            foreach (var tool in request.Tools)
            {
                declarations.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.InputSchema.GetRawText())
                });
            }
            obj["tools"] = new JsonArray
            {
                new JsonObject { ["functionDeclarations"] = declarations }
            };
        }

        var genConfig = new JsonObject();
        if (request.Parameters.MaxTokens.HasValue)
            genConfig["maxOutputTokens"] = request.Parameters.MaxTokens.Value;
        if (request.Parameters.Temperature.HasValue)
            genConfig["temperature"] = (double)request.Parameters.Temperature.Value;
        if (genConfig.Count > 0)
            obj["generationConfig"] = genConfig;

        return obj.ToJsonString();
    }

    private static ModelResponse ParseResponse(string json, ModelRequest request)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? text = null;
        var toolCalls = new List<ToolCall>();
        var stopReason = StopReason.EndTurn;
        var usage = TokenUsage.Zero;

        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];

            if (candidate.TryGetProperty("finishReason", out var fr))
                stopReason = MapStopReason(fr.GetString());

            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textEl))
                    {
                        text = textEl.GetString();
                    }
                    else if (part.TryGetProperty("functionCall", out var fc))
                    {
                        var name = fc.GetProperty("name").GetString() ?? "";
                        var argsEl = fc.TryGetProperty("args", out var a) ? a : default;
                        var argsJson = argsEl.ValueKind != JsonValueKind.Undefined
                            ? argsEl.GetRawText()
                            : "{}";
                        var id = $"call_{Guid.NewGuid():N}";
                        toolCalls.Add(new ToolCall(id, name, JsonDocument.Parse(argsJson).RootElement));
                    }
                }
            }
        }

        if (root.TryGetProperty("usageMetadata", out var meta))
        {
            var input = meta.TryGetProperty("promptTokenCount", out var pt) ? pt.GetInt32() : 0;
            var output = meta.TryGetProperty("candidatesTokenCount", out var ct2) ? ct2.GetInt32() : 0;
            usage = new TokenUsage(input, output);
        }

        return new ModelResponse(text, toolCalls, stopReason, usage);
    }

    /// <summary>
    /// Resolves the tool name for a <see cref="ToolResultBlock"/> by scanning the conversation
    /// for the matching <see cref="ToolUseBlock"/>. Gemini's functionResponse requires the name.
    /// </summary>
    private static string FindToolName(IReadOnlyList<Message> messages, string toolUseId)
    {
        foreach (var msg in messages)
        {
            foreach (var block in msg.Content)
            {
                if (block is ToolUseBlock tu && tu.Id == toolUseId)
                    return tu.Name;
            }
        }
        // Fallback: use the ID as the name (should not happen in well-formed conversations)
        return toolUseId;
    }

    private static StopReason MapStopReason(string? reason) => reason switch
    {
        "STOP" => StopReason.EndTurn,
        "MAX_TOKENS" => StopReason.MaxTokens,
        "TOOL_CALLS" => StopReason.ToolUse,
        "OTHER" => StopReason.StopSequence,
        _ => StopReason.EndTurn
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
