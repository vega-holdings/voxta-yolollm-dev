using System.Runtime.CompilerServices;
using System.IO;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Objects.Characters;
using Voxta.Abstractions.Chats.Objects.Chats;
using Voxta.Abstractions.Diagnostics;
using Voxta.Abstractions.Encryption;
using Voxta.Abstractions.Prompting;
using Voxta.Abstractions.Services;
using Voxta.Abstractions.Services.TextGen;
using Voxta.Abstractions.Tokenizers;
using Voxta.Abstractions.TextGenerationStreaming;
using Voxta.Model.Shared;
using Voxta.Modules.YoloLLM.Configuration;

namespace Voxta.Modules.YoloLLM.Services;

public class YoloTextGenService(
    IHttpClientFactory httpClientFactory,
    ILocalEncryptionProvider localEncryptionProvider,
    ILogger<YoloTextGenService> logger
) : ServiceBase(logger), ITextGenService
{
    private readonly ILocalEncryptionProvider _localEncryptionProvider = localEncryptionProvider;
    private YoloLlmSettings? _settings;
    private YoloLlmClient? _client;
    private string? _replySystemPrompt;

    public ITokenizer Tokenizer => NullTokenizer.Instance;

    public TextProcessingOptions TextProcessing => TextProcessingOptions.None;

    public int MaxWindowTokens => _settings?.MaxWindowTokens ?? 0;

    public int MaxTokens => _settings?.MaxNewTokens ?? 0;

    public int MaxMemoryTokens => _settings?.MaxMemoryTokens ?? 0;

    public SystemPromptOverrideTypes SystemPromptOverrideType => SystemPromptOverrideTypes.AddOn;

    public string? SystemPromptAddon => null;

    public string? ContextPromptAddon => null;

    public string? ReplyPrefix => null;

    protected override Task InitializeAsync(CancellationToken cancellationToken)
    {
        _settings = YoloLlmSettingsLoader.Load(ModuleConfiguration, ServiceSettings, _localEncryptionProvider);
        _client = new YoloLlmClient(httpClientFactory, logger, _settings);
        _replySystemPrompt = LoadPromptSafely(_settings.ReplySystemPromptPath, "reply");
        return Task.CompletedTask;
    }

    public Task WarmupAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public TextGenPreprocessingSettings CreateTextGenPreprocessingSettings(int maxSentences = 0, bool? allowMultipleLines = null)
    {
        return new TextGenPreprocessingSettings
        {
            MaxSentences = maxSentences,
            MaxWordsInAsterisks = 0,
            AllowMultipleLines = allowMultipleLines ?? true,
            TextProcessing = TextProcessing
        };
    }

    public bool CanProcessAttachments() => false;

    public async IAsyncEnumerable<LLMOutputToken> GenerateStreamingAsync(
        InferenceLogger observer,
        TextGenGenerateRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = _client ?? throw new InvalidOperationException("Service not initialized");
        var text = await client.GenerateAsync(request, cancellationToken);
        yield return new LLMOutputToken(text);
    }

    public async ValueTask<string> GenerateAsync(TextGenGenerateRequest request, InferenceLogger observer, CancellationToken cancellationToken = default)
    {
        var client = _client ?? throw new InvalidOperationException("Service not initialized");
        return await client.GenerateAsync(request, cancellationToken);
    }

    public async IAsyncEnumerable<LLMOutputToken> GenerateReplyAsync(
        IChatInferenceData chat,
        ICharacterInferenceData character,
        IPromptBuilder promptBuilder,
        string? prefix,
        GenerateConstraintRequest constraintRequest,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var constraints = await GetConstraintsAsync(chat, character, constraintRequest, cancellationToken);
        var request = await promptBuilder.CreateReplyRequest(chat, character, constraints, prefix, cancellationToken);
        request = ApplySystemOverride(request, _replySystemPrompt);
        var text = await GenerateInternalAsync(request, cancellationToken);
        yield return new LLMOutputToken(text);
    }

    public async IAsyncEnumerable<LLMOutputToken> GenerateStoryAsync(
        IChatInferenceData chat,
        string eventDescription,
        IPromptBuilder promptBuilder,
        string? prefix,
        GenerateConstraintRequest constraintRequest,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var constraints = await GetConstraintsAsync(chat, chat.GetMainCharacter(), constraintRequest, cancellationToken);
        var request = await promptBuilder.CreateStoryWriterRequest(chat, eventDescription, constraints, prefix, cancellationToken);
        request = ApplySystemOverride(request, _replySystemPrompt);
        var text = await GenerateInternalAsync(request, cancellationToken);
        yield return new LLMOutputToken(text);
    }

    public ValueTask<GenerateReplyConstraints> GetConstraintsAsync(
        IChatInferenceData chat,
        ICharacterInferenceData character,
        GenerateConstraintRequest constraintRequest,
        CancellationToken cancellationToken)
    {
        var settings = _settings ?? throw new InvalidOperationException("Service not initialized");
        var maxNewTokens = constraintRequest.MaxNewTokens > 0
            ? Math.Min(constraintRequest.MaxNewTokens, settings.MaxNewTokens)
            : settings.MaxNewTokens;

        return ValueTask.FromResult(new GenerateReplyConstraints
        {
            MaxInputTokens = settings.MaxWindowTokens,
            MaxNewTokens = maxNewTokens,
            MaxMemoryTokensRatio = settings.MaxMemoryTokens > 0 && settings.MaxWindowTokens > 0
                ? settings.MaxMemoryTokens / (double)settings.MaxWindowTokens
                : 0.25,
            FormattingStyle = ChatMessagesFormattingStyle.Normal,
            Reasoning = ReasoningStyle.None,
            PostHistorySupport = true,
            AllowMultipleLines = constraintRequest.AllowMultipleLines ?? true,
            TextProcessing = TextProcessing,
            Multimodal = false
        });
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
}
