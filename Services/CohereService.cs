using System.Text;

namespace ChatPortal2.Services;

public class CohereService
{
    private readonly IConfiguration _config;
    private readonly ILogger<CohereService> _logger;
    private readonly HttpClient _httpClient;
    private const string CohereApiUrl = "https://api.cohere.com/v2/chat";

    public CohereService(IConfiguration config, ILogger<CohereService> logger, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("cohere");
    }

    public async IAsyncEnumerable<string> StreamChatAsync(string userMessage, List<(string role, string content)> history, string systemPrompt = "You are a helpful data assistant.")
    {
        var apiKey = _config["Cohere:ApiKey"];
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_COHERE_API_KEY_HERE")
        {
            var mockObj = new
            {
                type = "data_response",
                prompt = userMessage.Length > 80 ? userMessage[..80] : userMessage,
                query = "SELECT region, SUM(total_revenue) AS total_revenue FROM sales GROUP BY region ORDER BY total_revenue DESC",
                description = "Mock response — configure Cohere:ApiKey in appsettings.json for real AI. Showing sample regional revenue data.",
                suggestedChart = "bar",
                suggestedFields = new { label = "region", value = "total_revenue" }
            };
            yield return Newtonsoft.Json.JsonConvert.SerializeObject(mockObj);
            yield break;
        }

        var messages = new List<object>();
        messages.Add(new { role = "system", content = systemPrompt });
        foreach (var (role, content) in history)
        {
            messages.Add(new { role = role, content = content });
        }
        messages.Add(new { role = "user", content = userMessage });

        var model = _config["Cohere:Model"] ?? "command-r-plus";
        var requestBody = new
        {
            model = model,
            messages = messages,
            stream = true
        };

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, CohereApiUrl);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        string? connectionError = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Cohere API returned {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
                connectionError = $"AI service error (HTTP {(int)response.StatusCode}). Please try again.";
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken.IsCancellationRequested == false)
        {
            _logger.LogError(ex, "Cohere API request timed out");
            connectionError = "The AI service request timed out. Please try again.";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Cohere API");
            connectionError = "Could not connect to the AI service. Please check your network and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Cohere API");
            connectionError = "Sorry, I encountered an error connecting to the AI service. Please try again.";
        }

        if (connectionError != null)
        {
            yield return connectionError;
            yield break;
        }

        using var stream = await response!.Content.ReadAsStreamAsync();
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            string? parsedText = null;
            try
            {
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(data);
                if (parsed?.type == "content-delta")
                {
                    parsedText = parsed?.delta?.message?.content?.text;
                }
            }
            catch
            {
                // Skip malformed SSE events
            }

            if (!string.IsNullOrEmpty(parsedText))
                yield return parsedText;
        }
    }

    public async IAsyncEnumerable<string> AnalyzeImageStreamAsync(string imageDataUrl, string prompt)
    {
        var apiKey = _config["Cohere:ApiKey"];
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_COHERE_API_KEY_HERE")
        {
            yield return "Chart analysis requires a valid Cohere API key.";
            yield break;
        }

        var effectivePrompt = string.IsNullOrWhiteSpace(prompt)
            ? "Analyze this chart. Describe the key trends, outliers, and actionable insights visible in the data."
            : prompt;

        var visionModel = _config["Cohere:VisionModel"] ?? "command-r-plus";
        var requestBody = new
        {
            model = visionModel,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = effectivePrompt },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = imageDataUrl, detail = "high" }
                        }
                    }
                }
            },
            stream = true
        };

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, CohereApiUrl);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        string? connectionError = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Cohere Vision API returned {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
                connectionError = $"AI service error (HTTP {(int)response.StatusCode}). Please try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call Cohere Vision API");
            connectionError = "Sorry, I encountered an error analyzing the chart. Please try again.";
        }

        if (connectionError != null)
        {
            yield return connectionError;
            yield break;
        }

        using var stream = await response!.Content.ReadAsStreamAsync();
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            string? parsedText = null;
            try
            {
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(data);
                if (parsed?.type == "content-delta")
                    parsedText = parsed?.delta?.message?.content?.text;
            }
            catch { }

            if (!string.IsNullOrEmpty(parsedText))
                yield return parsedText;
        }
    }

    public async Task<List<string>> ExtractMemoryAsync(string userMessage, string aiResponse, int maxFacts = 4)
    {
        var apiKey = _config["Cohere:ApiKey"];
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_COHERE_API_KEY_HERE")
            return new List<string>();

        var prompt =
            $"Extract up to {maxFacts} concise factual memories worth remembering from this conversation.\n" +
            "Only include genuinely useful facts about the user's goals, preferences, data context, or domain knowledge.\n" +
            "Return a JSON array of short strings only. Example: [\"User works with sales data\", \"Prefers bar charts\"]\n" +
            "If there is nothing worth remembering, return an empty array: []\n\n" +
            $"User: {userMessage}\n" +
            $"Assistant: {aiResponse}\n\n" +
            "JSON array:";

        var model = _config["Cohere:Model"] ?? "command-r-plus";
        var requestBody = new
        {
            model = model,
            messages = new object[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, CohereApiUrl);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(body);
            var text = (string?)parsed?.message?.content?[0]?.text ?? "";

            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');
            if (start == -1 || end <= start) return new List<string>();

            var arrayJson = text.Substring(start, end - start + 1);
            var facts = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(arrayJson);
            return facts ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory extraction failed");
            return new List<string>();
        }
    }
}
