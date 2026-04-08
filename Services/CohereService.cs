using System.Text;

namespace ChatPortal2.Services;

public class CohereService
{
    private readonly IConfiguration _config;
    private readonly ILogger<CohereService> _logger;
    private readonly HttpClient _httpClient;
    private const string CohereApiUrl = "https://api.cohere.ai/v2/chat";

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
            // Return a mock response when no API key is configured
            var mockResponse = "I'm a mock AI response. Please configure your Cohere API key in appsettings.json to enable real AI responses. Your query was: " + userMessage;
            foreach (var word in mockResponse.Split(' '))
            {
                yield return word + " ";
                await Task.Delay(30);
            }
            yield break;
        }

        var messages = new List<object>();
        messages.Add(new { role = "system", content = systemPrompt });
        foreach (var (role, content) in history)
        {
            messages.Add(new { role = role, content = content });
        }
        messages.Add(new { role = "user", content = userMessage });

        var requestBody = new
        {
            model = "command-a-03-2025",
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
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call Cohere API");
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
}
