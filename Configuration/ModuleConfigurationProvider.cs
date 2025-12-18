using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Model.Shared.Forms;

namespace Voxta.Modules.YoloLLM.Configuration;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global", Justification = "Fields reused in module registration.")]
public class ModuleConfigurationProvider(
    ILogger<ModuleConfigurationProvider> logger
) : ModuleConfigurationProviderBase, IModuleConfigurationProvider
{
    public static string[] FieldsRequiringReload =>
    [
        ApiKey.Name,
        BaseUrl.Name,
        Model.Name,
        Temperature.Name,
        MaxNewTokens.Name,
        MaxWindowTokens.Name,
        MaxMemoryTokens.Name,
        MaxSummaryTokens.Name,
        EnableGraphExtraction.Name,
        GraphExtractionPromptPath.Name,
        ReplySystemPromptPath.Name,
        SummaryPromptPath.Name,
        MemoryExtractionPromptPath.Name,
        LogLifecycleEvents.Name,
    ];

    public static readonly FormPasswordField ApiKey = new()
    {
        Name = "ApiKey",
        Label = "API Key",
        Text = "API key/token for the OpenAI-compatible endpoint (paste the raw token; no quotes; no `Bearer ` prefix).",
        Required = true,
    };

    public static readonly FormTextField BaseUrl = new()
    {
        Name = "BaseUrl",
        Label = "Base URL",
        Text = "Chat completion endpoint (OpenAI-compatible). Example (OpenRouter): `https://openrouter.ai/api/v1/chat/completions`",
        DefaultValue = "https://api.openai.com/v1/chat/completions",
        Placeholder = "https://your-endpoint/v1/chat/completions",
    };

    public static readonly FormTextField Model = new()
    {
        Name = "Model",
        Label = "Model",
        Text = "Model id for the LLM (example OpenRouter: `google/gemini-2.5-flash`).",
        DefaultValue = "gpt-4o-mini",
    };

    public static readonly FormDoubleSliderField Temperature = new()
    {
        Name = "Temperature",
        Label = "Temperature",
        Text = "Sampling temperature for generations.",
        DefaultValue = 0.7,
        Min = 0,
        Max = 2,
        SoftMin = 0,
        SoftMax = 1.5,
        Precision = 2,
    };

    public static readonly FormIntSliderField MaxNewTokens = new()
    {
        Name = "MaxNewTokens",
        Label = "Max New Tokens",
        Text = "Maximum tokens to generate for replies.",
        DefaultValue = 512,
        Min = 64,
        Max = 4096,
        SoftMin = 128,
        SoftMax = 2048,
    };

    public static readonly FormIntSliderField MaxWindowTokens = new()
    {
        Name = "MaxWindowTokens",
        Label = "Context Window Tokens",
        Text = "Approximate token budget for the full prompt window.",
        DefaultValue = 8192,
        Min = 2048,
        Max = 32768,
        SoftMin = 4096,
        SoftMax = 16384,
    };

    public static readonly FormIntSliderField MaxMemoryTokens = new()
    {
        Name = "MaxMemoryTokens",
        Label = "Memory Tokens Budget",
        Text = "Cap on memory/context tokens inside the window.",
        DefaultValue = 2048,
        Min = 256,
        Max = 8192,
        SoftMin = 512,
        SoftMax = 4096,
    };

    public static readonly FormIntSliderField MaxSummaryTokens = new()
    {
        Name = "MaxSummaryTokens",
        Label = "Summary Tokens",
        Text = "Cap on generated summary length.",
        DefaultValue = 512,
        Min = 64,
        Max = 4096,
        SoftMin = 128,
        SoftMax = 1024,
    };

    public static readonly FormDoubleSliderField SummarizationDigestRatio = new()
    {
        Name = "SummarizationDigestRatio",
        Label = "Summarization Digest Ratio",
        Text = "Ratio of chat tokens to summarize (smaller = more frequent digests).",
        DefaultValue = 0.35,
        Min = 0.05,
        Max = 1.0,
        SoftMin = 0.1,
        SoftMax = 0.5,
        Precision = 2,
    };

    public static readonly FormDoubleSliderField SummarizationTriggerMessagesBuffer = new()
    {
        Name = "SummarizationTriggerMessagesBuffer",
        Label = "Summarization Trigger Buffer",
        Text = "Minimum number of messages before triggering summarization.",
        DefaultValue = 2,
        Min = 0,
        Max = 10,
        SoftMin = 0,
        SoftMax = 5,
        Precision = 0,
    };

    public static readonly FormIntSliderField KeepLastMessages = new()
    {
        Name = "KeepLastMessages",
        Label = "Keep Last Messages",
        Text = "Always keep this many recent messages when summarizing.",
        DefaultValue = 4,
        Min = 0,
        Max = 20,
        SoftMin = 0,
        SoftMax = 10,
    };

    public static readonly FormBooleanField LogLifecycleEvents = new()
    {
        Name = "LogLifecycleEvents",
        Label = "Log Summary/Memory Events",
        Text = "If enabled, logs when summarization and memory extraction are invoked (helps debugging when these pipelines trigger).",
        DefaultValue = true,
    };

    public static readonly FormTextField ReplySystemPromptPath = new()
    {
        Name = "ReplySystemPromptPath",
        Label = "Reply System Prompt (optional)",
        Text = "File path to a system prompt that will be prepended for chat replies/story.",
        Placeholder = "Resources/Prompts/Default/en/YoloLLM/ReplySystemAddon.scriban",
        DefaultValue = "Resources/Prompts/Default/en/YoloLLM/ReplySystemAddon.scriban",
    };

    public static readonly FormTextField SummaryPromptPath = new()
    {
        Name = "SummaryPromptPath",
        Label = "Summarization Prompt (optional)",
        Text = "File path to a system prompt that will be prepended for summarization.",
        Placeholder = "Resources/Prompts/Default/en/YoloLLM/SummarizationAddon.scriban",
        DefaultValue = "Resources/Prompts/Default/en/YoloLLM/SummarizationAddon.scriban",
    };

    public static readonly FormTextField MemoryExtractionPromptPath = new()
    {
        Name = "MemoryExtractionPromptPath",
        Label = "Memory Extraction Prompt (optional)",
        Text = "File path to a system prompt that will be prepended for memory extraction.",
        Placeholder = "Resources/Prompts/Default/en/YoloLLM/MemoryExtractionAddon.scriban",
        DefaultValue = "Resources/Prompts/Default/en/YoloLLM/MemoryExtractionAddon.scriban",
    };

    public static readonly FormBooleanField EnableGraphExtraction = new()
    {
        Name = "EnableGraphExtraction",
        Label = "Enable Graph Extraction (separate LLM call)",
        Text = "If enabled, YOLOLLM runs an additional LLM call during summarization to extract graph JSON and writes it to the GraphMemory inbox (`Data/GraphMemory/Inbox`) for ingestion (without polluting long-term memory books).",
        DefaultValue = true,
    };

    public static readonly FormTextField GraphExtractionPromptPath = new()
    {
        Name = "GraphExtractionPromptPath",
        Label = "Graph Extraction Prompt (optional)",
        Text = "File path to the prompt template used for YOLOLLM's dedicated graph extraction call.",
        Placeholder = "Resources/Prompts/Default/en/YoloLLM/GraphExtraction.graph.scriban",
        DefaultValue = "Resources/Prompts/Default/en/YoloLLM/GraphExtraction.graph.scriban",
    };

    public Task<FormField[]> GetModuleConfigurationFieldsAsync(
        IAuthenticationContext auth,
        ISettingsSource settings,
        CancellationToken cancellationToken
    )
    {
        logger.LogDebug("Providing YOLO LLM module configuration fields");
        return Task.FromResult(FormBuilder.Build(
            FormTitleField.Create("YOLO LLM (TextGen + Summarization)", "Module-wide defaults. Service Settings presets can override these values.", false),
            ApiKey,
            BaseUrl,
            Model,
            Temperature,
            MaxNewTokens,
            MaxWindowTokens,
            MaxMemoryTokens,
            MaxSummaryTokens,
            SummarizationDigestRatio,
            SummarizationTriggerMessagesBuffer,
            KeepLastMessages,
            FormTitleField.Create("Prompt Overrides (optional)", null, false),
            ReplySystemPromptPath,
            SummaryPromptPath,
            MemoryExtractionPromptPath,
            EnableGraphExtraction,
            GraphExtractionPromptPath,
            LogLifecycleEvents
        ));
    }
}
