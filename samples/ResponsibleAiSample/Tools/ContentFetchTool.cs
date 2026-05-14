using StrandsAgents.Core;

namespace ResponsibleAiSample.Tools;

public partial class ContentFetchTool
{
    private static readonly HttpClient _httpClient = new();

    // RESPONSIBLE AI PRINCIPLE: Input Validation
    // The [ToolParameterValidation] attribute declares constraints that are enforced
    // by the ToolRegistry before this method is ever called. Invalid URLs are rejected
    // before any network request is made.
    [Tool("Fetches text content from a URL. Only HTTPS URLs up to 200 characters are allowed.")]
    public async Task<string> FetchContent(
        // INPUT VALIDATION: URL must be present, HTTPS only, and within a reasonable length
        [ToolParameterValidation(Required = true, MaxLength = 200, Pattern = "^https://")]
        string url)
    {
        // ERROR HANDLING: Wrap the HTTP call to return a descriptive error rather than throwing
        try
        {
            var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Return a truncated preview to avoid overwhelming the model context
            return content.Length > 2000
                ? content[..2000] + "\n[Content truncated at 2000 characters]"
                : content;
        }
        catch (Exception ex)
        {
            return $"Error fetching content: {ex.Message}";
        }
    }
}
