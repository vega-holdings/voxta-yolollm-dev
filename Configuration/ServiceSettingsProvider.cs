using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Model.Shared;
using Voxta.Model.Shared.Forms;

namespace Voxta.Modules.YoloLLM.Configuration;

public class ServiceSettingsProvider(
    ILogger<ServiceSettingsProvider> logger
) : IServiceSettingsProvider
{
    public static string[] FieldsRequiringReload =>
    [
        Model.Name,
        Temperature.Name,
        MaxNewTokens.Name,
        MaxWindowTokens.Name,
        MaxMemoryTokens.Name,
        MaxSummaryTokens.Name,
        SummarizationDigestRatio.Name,
        SummarizationTriggerMessagesBuffer.Name,
        KeepLastMessages.Name,
        ReplySystemPromptPath.Name,
        SummaryPromptPath.Name,
        MemoryExtractionPromptPath.Name,
        LogLifecycleEvents.Name,
    ];

    // These names intentionally match common module config field names. Service settings are stored separately per service type.
    public static readonly FormTextField Model = new() { Name = "Model", Label = "Model" };
    public static readonly FormDoubleSliderField Temperature = new() { Name = "Temperature", Label = "Temperature" };
    public static readonly FormIntSliderField MaxNewTokens = new() { Name = "MaxNewTokens", Label = "Max New Tokens" };
    public static readonly FormIntSliderField MaxWindowTokens = new() { Name = "MaxWindowTokens", Label = "Context Window Tokens" };
    public static readonly FormIntSliderField MaxMemoryTokens = new() { Name = "MaxMemoryTokens", Label = "Memory Tokens Budget" };
    public static readonly FormIntSliderField MaxSummaryTokens = new() { Name = "MaxSummaryTokens", Label = "Summary Tokens" };
    public static readonly FormDoubleSliderField SummarizationDigestRatio = new() { Name = "SummarizationDigestRatio", Label = "Summarization Digest Ratio" };
    public static readonly FormDoubleSliderField SummarizationTriggerMessagesBuffer = new() { Name = "SummarizationTriggerMessagesBuffer", Label = "Summarization Trigger Buffer" };
    public static readonly FormIntSliderField KeepLastMessages = new() { Name = "KeepLastMessages", Label = "Keep Last Messages" };
    public static readonly FormTextField ReplySystemPromptPath = new() { Name = "ReplySystemPromptPath", Label = "Reply System Prompt" };
    public static readonly FormTextField SummaryPromptPath = new() { Name = "SummaryPromptPath", Label = "Summarization Prompt" };
    public static readonly FormTextField MemoryExtractionPromptPath = new() { Name = "MemoryExtractionPromptPath", Label = "Memory Extraction Prompt" };
    public static readonly FormBooleanField LogLifecycleEvents = new() { Name = "LogLifecycleEvents", Label = "Log Summary/Memory Events" };

    public string? GetDefaultLabel(ServiceTypes serviceType, StaticSettingsSource settings)
    {
        var model = settings.GetOptional(Model);
        return string.IsNullOrWhiteSpace(model) ? null : model;
    }

    public Task<FormField[]> GetFormFieldsAsync(
        IAuthenticationContext auth,
        ServiceTypes serviceType,
        ISettingsSource moduleSettings,
        ISettingsSource serviceSettings,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Providing YOLO LLM service settings fields for {ServiceType}", serviceType);

        return serviceType switch
        {
            ServiceTypes.TextGen => Task.FromResult(GetTextGenFields(moduleSettings)),
            ServiceTypes.Summarization => Task.FromResult(GetTextGenFields(moduleSettings)),
            _ => Task.FromResult(Array.Empty<FormField>()),
        };
    }

    private static FormField[] GetTextGenFields(ISettingsSource moduleSettings)
    {
        return FormBuilder.Build(
            FormTitleField.Create("YOLO LLM Preset (TextGen + Summarization)", null, false),
            CreateModelField(moduleSettings),
            CreateTemperatureField(moduleSettings),
            CreateMaxNewTokensField(moduleSettings, "Maximum tokens to generate for replies (preset override)."),
            CreateMaxSummaryTokensField(moduleSettings),
            CreateSummarizationDigestRatioField(moduleSettings),
            CreateSummarizationTriggerMessagesBufferField(moduleSettings),
            CreateKeepLastMessagesField(moduleSettings),
            CreateMaxWindowTokensField(moduleSettings),
            CreateMaxMemoryTokensField(moduleSettings),
            FormTitleField.Create("Prompt Overrides (optional)", null, false),
            CreateReplySystemPromptPathField(moduleSettings),
            CreateSummaryPromptPathField(moduleSettings),
            CreateMemoryExtractionPromptPathField(moduleSettings),
            CreateLogLifecycleEventsField(moduleSettings)
        );
    }

    private static FormTextField CreateModelField(ISettingsSource moduleSettings)
    {
        return new FormTextField
        {
            Name = Model.Name,
            Label = "Model",
            Text = "Model id for the LLM (preset override).",
            DefaultValue = moduleSettings.GetRequired(ModuleConfigurationProvider.Model),
        };
    }

    private static FormDoubleSliderField CreateTemperatureField(ISettingsSource moduleSettings)
    {
        var moduleDefault = moduleSettings.GetOptional((FormNumberFieldBase<double>)ModuleConfigurationProvider.Temperature)
            ?? (double)(ModuleConfigurationProvider.Temperature.DefaultValue ?? 0.7);

        return new FormDoubleSliderField
        {
            Name = Temperature.Name,
            Label = "Temperature",
            Text = "Sampling temperature for generations (preset override).",
            DefaultValue = moduleDefault,
            Min = 0,
            Max = 2,
            SoftMin = 0,
            SoftMax = 1.5,
            Precision = 2,
        };
    }

    private static FormIntSliderField CreateMaxNewTokensField(ISettingsSource moduleSettings, string text)
    {
        return new FormIntSliderField
        {
            Name = MaxNewTokens.Name,
            Label = "Max New Tokens",
            Text = text,
            DefaultValue = moduleSettings.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxNewTokens),
            Min = 64,
            Max = 4096,
            SoftMin = 128,
            SoftMax = 2048,
        };
    }

    private static FormIntSliderField CreateMaxWindowTokensField(ISettingsSource moduleSettings)
    {
        return new FormIntSliderField
        {
            Name = MaxWindowTokens.Name,
            Label = "Context Window Tokens",
            Text = "Approximate token budget for the full prompt window (preset override).",
            DefaultValue = moduleSettings.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxWindowTokens),
            Min = 2048,
            Max = 32768,
            SoftMin = 4096,
            SoftMax = 16384,
        };
    }

    private static FormIntSliderField CreateMaxMemoryTokensField(ISettingsSource moduleSettings)
    {
        return new FormIntSliderField
        {
            Name = MaxMemoryTokens.Name,
            Label = "Memory Tokens Budget",
            Text = "Cap on memory/context tokens inside the window (preset override).",
            DefaultValue = moduleSettings.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxMemoryTokens),
            Min = 256,
            Max = 8192,
            SoftMin = 512,
            SoftMax = 4096,
        };
    }

    private static FormIntSliderField CreateMaxSummaryTokensField(ISettingsSource moduleSettings)
    {
        return new FormIntSliderField
        {
            Name = MaxSummaryTokens.Name,
            Label = "Summary Tokens",
            Text = "Cap on generated summary length (preset override).",
            DefaultValue = moduleSettings.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxSummaryTokens),
            Min = 64,
            Max = 4096,
            SoftMin = 128,
            SoftMax = 1024,
        };
    }

    private static FormDoubleSliderField CreateSummarizationDigestRatioField(ISettingsSource moduleSettings)
    {
        return new FormDoubleSliderField
        {
            Name = SummarizationDigestRatio.Name,
            Label = "Summarization Digest Ratio",
            Text = "Ratio of chat tokens to summarize (smaller = more frequent digests) (preset override).",
            DefaultValue = moduleSettings.GetRequired((FormNumberFieldBase<double>)ModuleConfigurationProvider.SummarizationDigestRatio),
            Min = 0.05,
            Max = 1.0,
            SoftMin = 0.1,
            SoftMax = 0.5,
            Precision = 2,
        };
    }

    private static FormDoubleSliderField CreateSummarizationTriggerMessagesBufferField(ISettingsSource moduleSettings)
    {
        return new FormDoubleSliderField
        {
            Name = SummarizationTriggerMessagesBuffer.Name,
            Label = "Summarization Trigger Buffer",
            Text = "Minimum number of messages before triggering summarization (preset override).",
            DefaultValue = moduleSettings.GetRequired((FormNumberFieldBase<double>)ModuleConfigurationProvider.SummarizationTriggerMessagesBuffer),
            Min = 0,
            Max = 10,
            SoftMin = 0,
            SoftMax = 5,
            Precision = 0,
        };
    }

    private static FormIntSliderField CreateKeepLastMessagesField(ISettingsSource moduleSettings)
    {
        return new FormIntSliderField
        {
            Name = KeepLastMessages.Name,
            Label = "Keep Last Messages",
            Text = "Always keep this many recent messages when summarizing (preset override).",
            DefaultValue = moduleSettings.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.KeepLastMessages),
            Min = 0,
            Max = 20,
            SoftMin = 0,
            SoftMax = 10,
        };
    }

    private static FormTextField CreateReplySystemPromptPathField(ISettingsSource moduleSettings)
    {
        return new FormTextField
        {
            Name = ReplySystemPromptPath.Name,
            Label = "Reply System Prompt (optional)",
            Text = "File path to a system prompt that will be prepended for chat replies/story (preset override).",
            Placeholder = ModuleConfigurationProvider.ReplySystemPromptPath.Placeholder,
            DefaultValue = moduleSettings.GetOptional(ModuleConfigurationProvider.ReplySystemPromptPath),
        };
    }

    private static FormTextField CreateSummaryPromptPathField(ISettingsSource moduleSettings)
    {
        return new FormTextField
        {
            Name = SummaryPromptPath.Name,
            Label = "Summarization Prompt (optional)",
            Text = "File path to a system prompt that will be prepended for summarization (preset override).",
            Placeholder = ModuleConfigurationProvider.SummaryPromptPath.Placeholder,
            DefaultValue = moduleSettings.GetOptional(ModuleConfigurationProvider.SummaryPromptPath),
        };
    }

    private static FormTextField CreateMemoryExtractionPromptPathField(ISettingsSource moduleSettings)
    {
        return new FormTextField
        {
            Name = MemoryExtractionPromptPath.Name,
            Label = "Memory Extraction Prompt (optional)",
            Text = "File path to a system prompt that will be prepended for memory extraction (preset override).",
            Placeholder = ModuleConfigurationProvider.MemoryExtractionPromptPath.Placeholder,
            DefaultValue = moduleSettings.GetOptional(ModuleConfigurationProvider.MemoryExtractionPromptPath),
        };
    }

    private static FormBooleanField CreateLogLifecycleEventsField(ISettingsSource moduleSettings)
    {
        return new FormBooleanField
        {
            Name = LogLifecycleEvents.Name,
            Label = "Log Summary/Memory Events",
            Text = "If enabled, logs when summarization and memory extraction are invoked (helps debugging when these pipelines trigger) (preset override).",
            DefaultValue = moduleSettings.GetRequired(ModuleConfigurationProvider.LogLifecycleEvents),
        };
    }
}
