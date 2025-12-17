using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    IInferenceLoggersManager inferenceLoggersManager,
    ILocalEncryptionProvider localEncryptionProvider,
    ILogger<YoloSummarizationService> logger
) : ServiceBase(logger), ISummarizationService
{
    private readonly IInferenceLoggersManager _inferenceLoggersManager = inferenceLoggersManager;
    private readonly ILocalEncryptionProvider _localEncryptionProvider = localEncryptionProvider;
    private YoloLlmSettings? _settings;
    private YoloLlmClient? _client;
    private string? _summaryPrompt;
    private string? _memoryExtractionPrompt;
    private string? _graphExtractionPrompt;

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
        _graphExtractionPrompt = LoadPromptSafely(_settings.GraphExtractionPromptPath, "graph-extraction");
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
        using var observer = TryRecord("Summarization");
        if (observer != null) observer.Request = new SimpleMessagesDisplayable(request.Messages);
        var result = await GenerateInternalAsync(request, cancellationToken);
        observer?.AddToken(new LLMOutputToken(result));
        observer?.Done();

        await MaybeGenerateAndQueueGraphAsync(chat, result, cancellationToken);

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
        using var observer = TryRecord("SpecializedSummarization");
        if (observer != null) observer.Request = new SimpleMessagesDisplayable(request.Messages);
        var result = await GenerateInternalAsync(request, cancellationToken);
        observer?.AddToken(new LLMOutputToken(result));
        observer?.Done();
        return result;
    }

    public async Task<string> ImagineAsync(
        IChatInferenceData chat,
        string? userPrompt,
        string? instructions,
        IPromptBuilder promptBuilder,
        CancellationToken cancellationToken)
    {
        var request = await promptBuilder.CreateImaginePromptGenRequest(chat, userPrompt, instructions, cancellationToken);
        using var observer = TryRecord("ImagePromptGen");
        if (observer != null) observer.Request = new SimpleMessagesDisplayable(request.Messages);
        var result = await GenerateInternalAsync(request, cancellationToken);
        observer?.AddToken(new LLMOutputToken(result));
        observer?.Done();
        return result;
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
        using var observer = TryRecord("MemoryExtraction");
        if (observer != null) observer.Request = new SimpleMessagesDisplayable(request.Messages);
        var text = await GenerateInternalAsync(request, cancellationToken);
        observer?.AddToken(new LLMOutputToken(text));
        observer?.Done();
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
        observer.Request ??= new SimpleMessagesDisplayable(request.Messages);
        var text = await GenerateInternalAsync(request, cancellationToken);
        var token = new LLMOutputToken(text);
        observer.AddToken(token);
        observer.Done();
        yield return token;
    }

    public async ValueTask<string> GenerateAsync(TextGenGenerateRequest request, InferenceLogger observer, CancellationToken cancellationToken = default)
    {
        observer.Request ??= new SimpleMessagesDisplayable(request.Messages);
        var result = await GenerateInternalAsync(request, cancellationToken);
        observer.AddToken(new LLMOutputToken(result));
        observer.Done();
        return result;
    }

    private async Task MaybeGenerateAndQueueGraphAsync(IChatInferenceData chat, string summary, CancellationToken cancellationToken)
    {
        var settings = _settings ?? throw new InvalidOperationException("Service not initialized");
        if (!settings.EnableGraphExtraction) return;
        if (string.IsNullOrWhiteSpace(summary)) return;

        var template = _graphExtractionPrompt;
        if (string.IsNullOrWhiteSpace(template))
        {
            logger.LogDebug("[YoloLLM] Graph extraction enabled but prompt template is not configured.");
            return;
        }

        string prompt;
        try
        {
            prompt = BuildGraphPrompt(template, chat, summary);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[YoloLLM] Graph prompt build failed; skipping graph extraction.");
            return;
        }

        try
        {
            var request = new TextGenGenerateRequest
            {
                Messages =
                [
                    new SimpleMessageData { Role = ChatMessageRole.System, Value = prompt },
                    new SimpleMessageData { Role = ChatMessageRole.User, Value = "Return the JSON only." },
                ],
                MaxNewTokens = 768,
            };

            using var observer = TryRecord("GraphExtraction");
            if (observer != null) observer.Request = new SimpleMessagesDisplayable(request.Messages);

            var response = await GenerateInternalAsync(request, cancellationToken);
            observer?.AddToken(new LLMOutputToken(response));
            observer?.Done();

            logger.LogDebug(
                "[YoloLLM] GraphExtraction prompt chatId={ChatId}:{NewLine}{Prompt}",
                chat.ChatId,
                Environment.NewLine,
                prompt);
            logger.LogDebug(
                "[YoloLLM] GraphExtraction raw response chatId={ChatId}:{NewLine}{Response}",
                chat.ChatId,
                Environment.NewLine,
                response);

            if (!TryBuildGraphJsonMemoryLine(chat, response, out var graphJsonLine, out var entitiesCount, out var relationsCount, out var failureReason))
            {
                if (settings.LogLifecycleEvents)
                {
                    logger.LogInformation(
                        "[YoloLLM] GraphExtraction produced no GRAPH_JSON (reason={Reason} entities={Entities} relations={Relations}) chatId={ChatId}",
                        failureReason, entitiesCount, relationsCount, chat.ChatId);
                }
                return;
            }

            TryWriteGraphMemoryInbox(chat.ChatId, graphJsonLine);

            logger.LogDebug(
                "[YoloLLM] GraphExtraction GRAPH_JSON payload chatId={ChatId}:{NewLine}{GraphJson}",
                chat.ChatId,
                Environment.NewLine,
                graphJsonLine);

            if (settings.LogLifecycleEvents)
            {
                logger.LogInformation("[YoloLLM] GraphExtraction wrote GRAPH_JSON to GraphMemory inbox chatId={ChatId}", chat.ChatId);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[YoloLLM] Graph extraction call failed; skipping graph output.");
        }
    }

    private static string BuildGraphPrompt(string template, IChatInferenceData chat, string input)
    {
        var existingNames = chat
            .GetCharacters()
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var prompt = template;
        prompt = ReplaceTemplateToken(prompt, "existingEntities", string.Join(", ", existingNames));
        prompt = ReplaceTemplateToken(prompt, "messages", input);
        return prompt;
    }

    private static string ReplaceTemplateToken(string template, string tokenName, string value)
    {
        if (string.IsNullOrEmpty(template)) return template;
        return Regex.Replace(
            template,
            @"\{\{\s*" + Regex.Escape(tokenName) + @"\s*\}\}",
            _ => value ?? string.Empty,
            RegexOptions.CultureInvariant);
    }

    private void TryWriteGraphMemoryInbox(Guid chatId, string graphJsonLine)
    {
        try
        {
            var inboxDir = Path.Combine(AppContext.BaseDirectory, "Data", "GraphMemory", "Inbox");
            Directory.CreateDirectory(inboxDir);

            var fileName = $"graph_{chatId:N}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.txt";
            var tmpPath = Path.Combine(inboxDir, fileName + ".tmp");
            var finalPath = Path.Combine(inboxDir, fileName);

            File.WriteAllText(tmpPath, graphJsonLine, Encoding.UTF8);
            File.Move(tmpPath, finalPath, overwrite: true);

            logger.LogDebug("[YoloLLM] Wrote graph update inbox file: {Path}", finalPath);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[YoloLLM] Failed to write graph update inbox file; graph viewer/context may be stale.");
        }
    }

    private InferenceLogger? TryRecord(string actionName)
    {
        try
        {
            return _inferenceLoggersManager.Record(ServiceType, InstanceSettings.ServiceName, actionName);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[YoloLLM] Inference logging unavailable for {Action}", actionName);
            return null;
        }
    }

    private static bool TryBuildGraphJsonMemoryLine(
        IChatInferenceData chat,
        string response,
        out string graphJsonLine,
        out int entitiesCount,
        out int relationsCount,
        out string failureReason)
    {
        graphJsonLine = string.Empty;
        entitiesCount = 0;
        relationsCount = 0;
        failureReason = "unknown";

        if (string.IsNullOrWhiteSpace(response))
        {
            failureReason = "empty_response";
            return false;
        }

        var text = StripCodeFences(response);
        var hasOpenBrace = text.IndexOf('{') >= 0;
        var sawCandidate = false;
        var sawValidJson = false;
        var sawGraphPayload = false;
        var sawEmptyGraph = false;

        foreach (var candidate in EnumerateJsonObjectCandidates(text))
        {
            sawCandidate = true;
            try
            {
                using var doc = JsonDocument.Parse(candidate, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                sawValidJson = true;

                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                var entities = ExtractEntityItems(root, out var sawEntitiesKey);
                var relations = ExtractRelationItems(root, out var sawRelationsKey);
                if (!sawEntitiesKey && !sawRelationsKey && entities.Count == 0 && relations.Count == 0) continue;
                sawGraphPayload = true;

                entitiesCount = entities.Count;
                relationsCount = relations.Count;

                if (entitiesCount == 0 && relationsCount == 0)
                {
                    // Still emit a graph update so GraphMemory can ingest meta (participants/chat scope),
                    // which is especially important for group chats where a character may join mid-session.
                    sawEmptyGraph = true;
                }

                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();

                    // Meta is injected by the module (not the LLM) so GraphMemory can scope graph writes
                    // even when Voxta integrates the same memories across multiple character books.
                    writer.WritePropertyName("meta");
                    writer.WriteStartObject();

                    writer.WriteString("chatId", chat.ChatId);
                    writer.WriteString("sessionId", chat.SessionId);

                    writer.WritePropertyName("user");
                    writer.WriteStartObject();
                    writer.WriteString("id", chat.User.Id);
                    writer.WriteString("name", chat.User.Name);
                    writer.WriteEndObject();

                    writer.WritePropertyName("characters");
                    writer.WriteStartArray();
                    foreach (var c in chat.GetCharacters())
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", c.Id);
                        writer.WriteString("name", c.Name);
                        writer.WriteString("role", c.Role.ToString());
                        if (!string.IsNullOrWhiteSpace(c.ScenarioRole))
                        {
                            writer.WriteString("scenarioRole", c.ScenarioRole);
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();

                    writer.WriteEndObject(); // meta

                    writer.WritePropertyName("entities");
                    writer.WriteStartArray();
                    foreach (var entity in entities)
                    {
                        WriteEntityItem(writer, entity);
                    }
                    writer.WriteEndArray();

                    writer.WritePropertyName("relations");
                    writer.WriteStartArray();
                    foreach (var relation in relations)
                    {
                        WriteRelationItem(writer, relation);
                    }
                    writer.WriteEndArray();

                    writer.WriteEndObject();
                }

                var compact = Encoding.UTF8.GetString(stream.ToArray());
                graphJsonLine = $"GRAPH_JSON: {compact}";
                failureReason = "";
                return true;
            }
            catch (JsonException)
            {
                // try next candidate
            }
        }

        if (sawEmptyGraph)
        {
            failureReason = "empty_arrays";
        }

        if (sawValidJson && !sawGraphPayload)
        {
            failureReason = "missing_arrays";
        }
        else if (sawCandidate)
        {
            failureReason = "invalid_json";
        }
        else
        {
            failureReason = hasOpenBrace ? "truncated_json" : "no_json_object";
        }

        return false;
    }

    private static List<JsonElement> ExtractEntityItems(JsonElement root, out bool sawEntitiesKey)
    {
        sawEntitiesKey = TryGetAnyPropertyIgnoreCase(root, ["entities", "characters", "entity"], out var entitiesEl);
        if (sawEntitiesKey)
        {
            return CoerceToList(entitiesEl, IsValidEntityItem);
        }

        // Heuristic fallback: find the array property that looks most like an entities list.
        return FindBestArrayProperty(root, IsValidEntityItem);
    }

    private static List<JsonElement> ExtractRelationItems(JsonElement root, out bool sawRelationsKey)
    {
        sawRelationsKey = TryGetAnyPropertyIgnoreCase(root, ["relations", "relationships"], out var relationsEl);
        if (sawRelationsKey)
        {
            return CoerceToList(relationsEl, IsValidRelationItem);
        }

        // Heuristic fallback: find the array property that looks most like a relations list.
        return FindBestArrayProperty(root, IsValidRelationItem);
    }

    private static List<JsonElement> FindBestArrayProperty(JsonElement root, Func<JsonElement, bool> predicate)
    {
        if (root.ValueKind != JsonValueKind.Object) return [];

        List<JsonElement> best = [];
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;
            var items = prop.Value.EnumerateArray().Where(predicate).ToList();
            if (items.Count > best.Count) best = items;
        }
        return best;
    }

    private static List<JsonElement> CoerceToList(JsonElement element, Func<JsonElement, bool> predicate)
    {
        var items = new List<JsonElement>();

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (predicate(item)) items.Add(item);
            }
            return items;
        }

        if (predicate(element)) items.Add(element);
        return items;
    }

    private static bool IsValidEntityItem(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return !string.IsNullOrWhiteSpace(element.GetString());
        }

        if (element.ValueKind != JsonValueKind.Object) return false;
        return TryGetStringPropertyIgnoreCase(element, "name", out var name) && !string.IsNullOrWhiteSpace(name);
    }

    private static bool IsValidRelationItem(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!TryGetStringPropertyIgnoreCase(element, "source", out var source) || string.IsNullOrWhiteSpace(source)) return false;
        if (!TryGetStringPropertyIgnoreCase(element, "target", out var target) || string.IsNullOrWhiteSpace(target)) return false;
        if (!TryGetStringPropertyIgnoreCase(element, "relation", out var relation) || string.IsNullOrWhiteSpace(relation))
        {
            if (!TryGetStringPropertyIgnoreCase(element, "type", out relation) || string.IsNullOrWhiteSpace(relation)) return false;
        }
        return true;
    }

    private static void WriteEntityItem(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var entityName = element.GetString();
            if (!string.IsNullOrWhiteSpace(entityName)) writer.WriteStringValue(entityName);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object) return;
        if (!TryGetStringPropertyIgnoreCase(element, "name", out var entityNameProp) || string.IsNullOrWhiteSpace(entityNameProp)) return;

        writer.WriteStartObject();
        writer.WriteString("name", entityNameProp);

        if (TryGetStringPropertyIgnoreCase(element, "type", out var type) && !string.IsNullOrWhiteSpace(type))
        {
            writer.WriteString("type", type);
        }

        if (TryGetStringPropertyIgnoreCase(element, "summary", out var summary) && !string.IsNullOrWhiteSpace(summary))
        {
            writer.WriteString("summary", summary);
        }

        if (TryGetPropertyIgnoreCase(element, "state", out var state) && state.ValueKind == JsonValueKind.Object)
        {
            writer.WritePropertyName("state");
            state.WriteTo(writer);
        }

        if (TryGetPropertyIgnoreCase(element, "aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array)
        {
            writer.WritePropertyName("aliases");
            aliases.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteRelationItem(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        if (!TryGetStringPropertyIgnoreCase(element, "source", out var source) || string.IsNullOrWhiteSpace(source)) return;
        if (!TryGetStringPropertyIgnoreCase(element, "target", out var target) || string.IsNullOrWhiteSpace(target)) return;

        string? relation = null;
        if (TryGetStringPropertyIgnoreCase(element, "relation", out var rel) && !string.IsNullOrWhiteSpace(rel))
        {
            relation = rel;
        }
        else if (TryGetStringPropertyIgnoreCase(element, "type", out var type) && !string.IsNullOrWhiteSpace(type))
        {
            relation = type;
        }

        if (string.IsNullOrWhiteSpace(relation)) return;

        writer.WriteStartObject();
        writer.WriteString("source", source);
        writer.WriteString("target", target);
        writer.WriteString("relation", relation);

        if (TryGetPropertyIgnoreCase(element, "attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object)
        {
            writer.WritePropertyName("attributes");
            attributes.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static bool TryGetAnyPropertyIgnoreCase(JsonElement obj, string[] names, out JsonElement value)
    {
        foreach (var name in names)
        {
            if (TryGetPropertyIgnoreCase(obj, name, out value)) return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetStringPropertyIgnoreCase(JsonElement obj, string name, out string? value)
    {
        value = null;
        if (!TryGetPropertyIgnoreCase(obj, name, out var element)) return false;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;

        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateJsonObjectCandidates(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        var inString = false;
        var escape = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c != '{') continue;
            if (!TryFindMatchingBrace(text, i, out var end)) continue;

            yield return text.Substring(i, end - i + 1);
        }
    }

    private static bool TryFindMatchingBrace(string text, int start, out int end)
    {
        end = -1;
        if (start < 0 || start >= text.Length) return false;
        if (text[start] != '{') return false;

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                depth++;
                continue;
            }

            if (c != '}') continue;

            depth--;
            if (depth == 0)
            {
                end = i;
                return true;
            }
        }

        return false;
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
                .Where(l => !IsGraphJsonLine(l))
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
                        var trimmed = value!.Trim();
                        if (IsGraphJsonLine(trimmed)) continue;
                        results.Add(new MemoryExtractResult { Index = index++, Text = trimmed });
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
            .Where(l => !l.Equals("<memories>", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.Equals("</memories>", StringComparison.OrdinalIgnoreCase))
            .Where(l => !IsGraphJsonLine(l))
            .ToArray();

        if (lines.Length == 0) return Array.Empty<MemoryExtractResult>();

        return lines
            .Select(NormalizeMemoryLine)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Where(l => !IsGraphJsonLine(l!))
            .Select((line, i) => new MemoryExtractResult { Index = i, Text = line! })
            .ToArray();
    }

    private static bool IsGraphJsonLine(string text)
    {
        return text.TrimStart().StartsWith("GRAPH_JSON:", StringComparison.OrdinalIgnoreCase);
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
