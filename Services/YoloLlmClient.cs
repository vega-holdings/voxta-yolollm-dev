using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Services.TextGen;
using Voxta.Model.Shared;

namespace Voxta.Modules.YoloLLM.Services;

internal class YoloLlmClient(
    IHttpClientFactory httpClientFactory,
    ILogger logger,
    YoloLlmSettings settings)
{
    private bool _didWarnKeyNormalized;

    public async Task<string> GenerateAsync(TextGenGenerateRequest request, CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient(nameof(YoloLlmClient));
        using var message = new HttpRequestMessage(HttpMethod.Post, settings.BaseUrl);

        var apiKey = NormalizeApiKey(settings.ApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("YOLO LLM ApiKey is empty; check module configuration.");
            throw new InvalidOperationException("YOLO LLM ApiKey is empty.");
        }

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = BuildPayload(request, out var usedMaxTokens);
        var json = JsonSerializer.Serialize(payload);
        message.Content = new StringContent(json, Encoding.UTF8, "application/json");

        logger.LogDebug("Sending LLM request to {Url} with max_tokens={MaxTokens}", settings.BaseUrl, usedMaxTokens);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("LLM call failed: {Status} {Reason} {Body}", (int)response.StatusCode, response.ReasonPhrase, body);
            response.EnsureSuccessStatusCode();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            logger.LogWarning("LLM response missing choices, returning empty string");
            return string.Empty;
        }

        var content = choices[0].GetProperty("message").GetProperty("content").GetString();
        return content?.Trim() ?? string.Empty;
    }

    private void LogKeyNormalization(string originalKey, string normalizedKey)
    {
        logger.LogWarning("YOLO LLM ApiKey appears to include extra wrapping/whitespace; normalized key length {Before}->{After}. " +
                          "Ensure you paste the raw OpenRouter token (no quotes, no Bearer prefix).",
            originalKey.Length, normalizedKey.Length);
    }

    private string NormalizeApiKey(string? apiKey)
    {
        var original = apiKey ?? string.Empty;
        var key = original.Trim();

        if (key.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            key = key.Substring("Bearer ".Length).Trim();
        }

        // Common paste formats: "sk-..." or 'sk-...' or `sk-...`
        if (key.Length >= 2)
        {
            var first = key[0];
            var last = key[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\'') || (first == '`' && last == '`'))
            {
                key = key.Substring(1, key.Length - 2).Trim();
            }
        }

        // Remove any remaining whitespace (newlines from copy/paste)
        key = string.Concat(key.Where(c => !char.IsWhiteSpace(c)));

        if (!_didWarnKeyNormalized && !string.Equals(original, key, StringComparison.Ordinal))
        {
            LogKeyNormalization(original, key);
            _didWarnKeyNormalized = true;
        }

        return key;
    }

    private object BuildPayload(TextGenGenerateRequest request, out int maxNewTokens)
    {
        // `MaxNewTokens` is the reply cap; `MaxSummaryTokens` is the summarization/memory cap.
        // Treat the larger of the two as a safety upper-bound, but default to the reply cap when the request doesn't specify.
        var upperBound = Math.Max(settings.MaxNewTokens, settings.MaxSummaryTokens);
        var desired = request.MaxNewTokens > 0 ? request.MaxNewTokens : settings.MaxNewTokens;
        maxNewTokens = Math.Min(desired, upperBound);

        return new
        {
            model = settings.Model,
            temperature = settings.Temperature,
            max_tokens = maxNewTokens,
            messages = request.Messages.Select(m => new
            {
                role = MapRole(m.Role),
                content = m.Value
            }),
            stop = request.StoppingStrings.Length > 0 ? request.StoppingStrings : null
        };
    }

    private static string MapRole(ChatMessageRole role) =>
        role switch
        {
            ChatMessageRole.System => "system",
            ChatMessageRole.Assistant => "assistant",
            ChatMessageRole.User => "user",
            _ => "user"
        };
}
