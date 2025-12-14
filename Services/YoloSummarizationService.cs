using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Objects;
using Voxta.Abstractions.Chats.Objects.Characters;
using Voxta.Abstractions.Chats.Objects.Chats;
using Voxta.Abstractions.Diagnostics;
using Voxta.Abstractions.Encryption;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Prompting;
using Voxta.Abstractions.Services;
using Voxta.Abstractions.Services.Summarization;
using Voxta.Abstractions.Services.TextGen;
using Voxta.Abstractions.Tokenizers;
using Voxta.Model.Shared;
using Voxta.Modules.YoloLLM.Configuration;
using System.IO;

namespace Voxta.Modules.YoloLLM.Services;

public class YoloSummarizationService(
    IHttpClientFactory httpClientFactory,
    ILocalEncryptionProvider localEncryptionProvider,
    ILogger<YoloSummarizationService> logger
) : ServiceBase(logger), ISummarizationService
{
    private readonly ILocalEncryptionProvider _localEncryptionProvider = localEncryptionProvider;
    private YoloLlmSettings? _settings;
    private YoloLlmClient? _client;
    private string? _summaryPrompt;
    private string? _memoryExtractionPrompt;

    public ITokenizer Tokenizer => NullTokenizer.Instance;

    public double SummarizationDigestRatio => _settings?.SummarizationDigestRatio ?? 0.3;

    public double SummarizationTriggerMessagesBuffer => _settings?.SummarizationTriggerMessagesBuffer ?? 2;

    public int TokenWindow => _settings?.MaxWindowTokens ?? 0;

    public int MaxSummaryLength => _settings?.MaxSummaryTokens ?? 0;

    public int KeepLastMessages => _settings?.KeepLastMessages ?? 0;

    protected override Task InitializeAsync(CancellationToken cancellationToken)
    {
        _settings = YoloLlmSettingsLoader.Load(ModuleConfiguration, ServiceSettings, _localEncryptionProvider);
        _client = new YoloLlmClient(httpClientFactory, logger, _settings);
        _summaryPrompt = LoadPromptSafely(_settings.SummaryPromptPath, "summary");
        _memoryExtractionPrompt = LoadPromptSafely(_settings.MemoryExtractionPromptPath, "memory-extraction");
        return Task.CompletedTask;
    }

    public Task WarmupAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async ValueTask<string> SummarizeAsync(
        IChatInferenceData chat,
        IPromptBuilder promptBuilder,
        IReadOnlyList<ChatMessageData> messagesToSummarize,
        CancellationToken cancellationToken)
    {
        var settings = _settings ?? throw new InvalidOperationException("Service not initialized");
        var sw = settings.LogLifecycleEvents ? Stopwatch.StartNew() : null;
        if (settings.LogLifecycleEvents)
        {
            logger.LogInformation("[YoloLLM] SummarizeAsync start chatId={ChatId} sessionId={SessionId} messages={Count} tokenWindow={TokenWindow} maxSummaryTokens={MaxSummaryTokens}",
                chat.ChatId, chat.SessionId, messagesToSummarize.Count, TokenWindow, MaxSummaryLength);
        }

        var request = await promptBuilder.CreateSummarizationRequest(chat, messagesToSummarize, cancellationToken);
        request = ApplySystemOverride(request, _summaryPrompt);
        request = ApplySummaryCaps(request);
        var result = await GenerateInternalAsync(request, cancellationToken);

        if (settings.LogLifecycleEvents)
        {
            logger.LogInformation("[YoloLLM] SummarizeAsync done chatId={ChatId} chars={Chars} ms={Ms}",
                chat.ChatId, result.Length, sw?.ElapsedMilliseconds ?? 0);
        }

        return result;
    }

    public async ValueTask<string> SummarizeAsync(string prompt, CancellationToken cancellationToken)
    {
        var request = new TextGenGenerateRequest
        {
            Messages =
            [
                new SimpleMessageData { Role = ChatMessageRole.System, Value = "Summarize the following text." },
                new SimpleMessageData { Role = ChatMessageRole.User, Value = prompt }
            ],
            MaxNewTokens = _settings?.MaxSummaryTokens ?? 512
        };
        return await GenerateInternalAsync(request, cancellationToken);
    }

    public async Task<string> ImagineAsync(
        IChatInferenceData chat,
        string? userPrompt,
        string? instructions,
        IPromptBuilder promptBuilder,
        CancellationToken cancellationToken)
    {
        var request = await promptBuilder.CreateImaginePromptGenRequest(chat, userPrompt, instructions, cancellationToken);
        return await GenerateInternalAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryExtractResult>> ExtractMemoriesAsync(
        IChatInferenceData chat,
        ICharacterInferenceData character,
        IPromptBuilder promptBuilder,
        IReadOnlyList<IChatMessageData> messages,
        CancellationToken cancellationToken)
    {
        var settings = _settings ?? throw new InvalidOperationException("Service not initialized");
        var sw = settings.LogLifecycleEvents ? Stopwatch.StartNew() : null;
        if (settings.LogLifecycleEvents)
        {
            logger.LogInformation("[YoloLLM] ExtractMemoriesAsync start chatId={ChatId} sessionId={SessionId} messages={Count} maxSummaryTokens={MaxSummaryTokens}",
                chat.ChatId, chat.SessionId, messages.Count, MaxSummaryLength);
        }

        var request = await promptBuilder.CreateMemoryExtractionRequest(chat, character, messages, cancellationToken);
        request = ApplySystemOverride(request, _memoryExtractionPrompt);
        request = ApplySummaryCaps(request);
        var text = await GenerateInternalAsync(request, cancellationToken);
        var extracted = ParseMemoryExtractResult(text);

        if (settings.LogLifecycleEvents)
        {
            logger.LogInformation("[YoloLLM] ExtractMemoriesAsync done chatId={ChatId} extracted={Count} ms={Ms}",
                chat.ChatId, extracted.Count, sw?.ElapsedMilliseconds ?? 0);
        }

        return extracted;
    }

    public Task<MemoryMergeResult> MergeMemoriesAsync(
        IChatInferenceData chat,
        ICharacterInferenceData character,
        IPromptBuilder promptBuilder,
        IReadOnlyList<MemoryItem> memories,
        CancellationToken cancellationToken)
    {
        // YOLO: no-op merge until a richer merge prompt/format is defined.
        return Task.FromResult(MemoryMergeResult.Empty);
    }

    public async IAsyncEnumerable<LLMOutputToken> GenerateStreamingAsync(
        InferenceLogger observer,
        TextGenGenerateRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var text = await GenerateInternalAsync(request, cancellationToken);
        yield return new LLMOutputToken(text);
    }

    public async ValueTask<string> GenerateAsync(TextGenGenerateRequest request, InferenceLogger observer, CancellationToken cancellationToken = default)
    {
        return await GenerateInternalAsync(request, cancellationToken);
    }

    private TextGenGenerateRequest ApplySummaryCaps(TextGenGenerateRequest request)
    {
        if (_settings is null) throw new InvalidOperationException("Service not initialized");
        var max = _settings.MaxSummaryTokens;
        var maxTokens = request.MaxNewTokens > 0 ? Math.Min(request.MaxNewTokens, max) : max;
        return new TextGenGenerateRequest
        {
            Type = request.Type,
            UserName = request.UserName,
            AssistantName = request.AssistantName,
            Messages = request.Messages,
            FormattingAndContinuationPrefix = request.FormattingAndContinuationPrefix,
            ContinuationPrefill = request.ContinuationPrefill,
            StoppingStrings = request.StoppingStrings,
            MaxNewTokens = maxTokens
        };
    }

    private async Task<string> GenerateInternalAsync(TextGenGenerateRequest request, CancellationToken cancellationToken)
    {
        var client = _client ?? throw new InvalidOperationException("Service not initialized");
        return await client.GenerateAsync(request, cancellationToken);
    }

    private static TextGenGenerateRequest ApplySystemOverride(TextGenGenerateRequest request, string? systemPrompt)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt)) return request;
        var messages = request.Messages;
        var merged = new List<SimpleMessageData>(messages.Length + 1)
        {
            new SimpleMessageData { Role = ChatMessageRole.System, Value = systemPrompt }
        };
        merged.AddRange(messages);
        return new TextGenGenerateRequest
        {
            Type = request.Type,
            UserName = request.UserName,
            AssistantName = request.AssistantName,
            Messages = merged.ToArray(),
            FormattingAndContinuationPrefix = request.FormattingAndContinuationPrefix,
            ContinuationPrefill = request.ContinuationPrefill,
            StoppingStrings = request.StoppingStrings,
            MaxNewTokens = request.MaxNewTokens
        };
    }

    private string? LoadPromptSafely(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (!File.Exists(path))
            {
                logger.LogWarning("Configured {Label} prompt path not found: {Path}", label, path);
                return null;
            }
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load {Label} prompt from {Path}", label, path);
            return null;
        }
    }

    private static IReadOnlyList<MemoryExtractResult> ParseMemoryExtractResult(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<MemoryExtractResult>();

        text = StripCodeFences(text);

        // If the model used the default Voxta prompt style: <memories> ... </memories>
        var memories = TryExtractMemoriesBlock(text);
        if (memories != null)
        {
            return memories
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select((line, i) => new MemoryExtractResult { Index = i, Text = line })
                .ToArray();
        }

        // Try JSON array first
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var results = new List<MemoryExtractResult>();
                var index = 0;
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var value = element.ValueKind switch
                    {
                        JsonValueKind.Object when element.TryGetProperty("text", out var textProp) => textProp.GetString(),
                        JsonValueKind.String => element.GetString(),
                        _ => null
                    };
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        results.Add(new MemoryExtractResult { Index = index++, Text = value!.Trim() });
                    }
                }
                if (results.Count > 0) return results;
            }
        }
        catch (JsonException)
        {
            // fall back to line-based parsing
        }

        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Where(l => !l.StartsWith("GRAPH_JSON:", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.Equals("<memories>", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.Equals("</memories>", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (lines.Length == 0) return Array.Empty<MemoryExtractResult>();

        return lines
            .Select(NormalizeMemoryLine)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select((line, i) => new MemoryExtractResult { Index = i, Text = line! })
            .ToArray();
    }

    private static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return text;

        // Remove the leading ```lang and trailing ``` if present.
        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0) return text;
        var withoutHeader = trimmed.Substring(firstNewline + 1);
        var endFence = withoutHeader.LastIndexOf("```", StringComparison.Ordinal);
        if (endFence < 0) return text;
        return withoutHeader.Substring(0, endFence).Trim();
    }

    private static IReadOnlyList<string>? TryExtractMemoriesBlock(string text)
    {
        var start = text.IndexOf("<memories>", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var end = text.IndexOf("</memories>", StringComparison.OrdinalIgnoreCase);
        if (end < 0 || end <= start) return null;

        var inner = text.Substring(start + "<memories>".Length, end - (start + "<memories>".Length));
        var lines = inner
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeMemoryLine)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!)
            .ToList();

        return lines.Count == 0 ? null : lines;
    }

    private static string? NormalizeMemoryLine(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        if (trimmed.StartsWith("GRAPH_JSON:", StringComparison.OrdinalIgnoreCase)) return null;

        // Common formats from Voxta prompts: "1; text" or "1: text"
        var semi = trimmed.IndexOf(';');
        if (semi > 0 && int.TryParse(trimmed.Substring(0, semi).Trim(), out _))
        {
            return trimmed.Substring(semi + 1).Trim();
        }
        var colon = trimmed.IndexOf(':');
        if (colon > 0 && int.TryParse(trimmed.Substring(0, colon).Trim(), out _))
        {
            return trimmed.Substring(colon + 1).Trim();
        }
        return trimmed;
    }
}
