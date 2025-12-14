using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Encryption;
using Voxta.Model.Shared.Forms;
using Voxta.Modules.YoloLLM.Configuration;

namespace Voxta.Modules.YoloLLM.Services;

internal record YoloLlmSettings
{
    public required string ApiKey { get; init; }
    public required string BaseUrl { get; init; }
    public required string Model { get; init; }
    public double Temperature { get; init; }
    public int MaxNewTokens { get; init; }
    public int MaxWindowTokens { get; init; }
    public int MaxMemoryTokens { get; init; }
    public int MaxSummaryTokens { get; init; }
    public double SummarizationDigestRatio { get; init; }
    public double SummarizationTriggerMessagesBuffer { get; init; }
    public int KeepLastMessages { get; init; }
    public string? ReplySystemPromptPath { get; init; }
    public string? SummaryPromptPath { get; init; }
    public string? MemoryExtractionPromptPath { get; init; }
    public bool EnableGraphExtraction { get; init; }
    public string? GraphExtractionPromptPath { get; init; }
    public bool LogLifecycleEvents { get; init; }
}

internal static class YoloLlmSettingsLoader
{
    public static YoloLlmSettings Load(
        ISettingsSource moduleConfiguration,
        ISettingsSource serviceSettings,
        ILocalEncryptionProvider localEncryptionProvider)
    {
        var encryptedApiKey = moduleConfiguration.GetRequired(ModuleConfigurationProvider.ApiKey);
        var apiKey = TryDecrypt(localEncryptionProvider, encryptedApiKey);

        var moduleModel = moduleConfiguration.GetRequired(ModuleConfigurationProvider.Model);
        var moduleTemperature = (double)(moduleConfiguration.GetOptional((FormNumberFieldBase<double>)ModuleConfigurationProvider.Temperature)
            ?? ModuleConfigurationProvider.Temperature.DefaultValue
            ?? 0.7);
        var moduleMaxNewTokens = moduleConfiguration.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxNewTokens);
        var moduleMaxWindowTokens = moduleConfiguration.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxWindowTokens);
        var moduleMaxMemoryTokens = moduleConfiguration.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxMemoryTokens);
        var moduleMaxSummaryTokens = moduleConfiguration.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxSummaryTokens);
        var moduleSummarizationDigestRatio = moduleConfiguration.GetRequired((FormNumberFieldBase<double>)ModuleConfigurationProvider.SummarizationDigestRatio);
        var moduleSummarizationTriggerMessagesBuffer = moduleConfiguration.GetRequired((FormNumberFieldBase<double>)ModuleConfigurationProvider.SummarizationTriggerMessagesBuffer);
        var moduleKeepLastMessages = moduleConfiguration.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.KeepLastMessages);
        var moduleReplySystemPromptPath = moduleConfiguration.GetOptional(ModuleConfigurationProvider.ReplySystemPromptPath);
        var moduleSummaryPromptPath = moduleConfiguration.GetOptional(ModuleConfigurationProvider.SummaryPromptPath);
        var moduleMemoryExtractionPromptPath = moduleConfiguration.GetOptional(ModuleConfigurationProvider.MemoryExtractionPromptPath);
        var moduleEnableGraphExtraction = moduleConfiguration.GetRequired(ModuleConfigurationProvider.EnableGraphExtraction);
        var moduleGraphExtractionPromptPath = moduleConfiguration.GetOptional(ModuleConfigurationProvider.GraphExtractionPromptPath);
        var moduleLogLifecycleEvents = moduleConfiguration.GetRequired(ModuleConfigurationProvider.LogLifecycleEvents);

        var model = serviceSettings.HasValue(Configuration.ServiceSettingsProvider.Model)
            ? serviceSettings.GetRequired(Configuration.ServiceSettingsProvider.Model)
            : moduleModel;

        var temperature = serviceSettings.HasValue((FormNumberFieldBase<double>)Configuration.ServiceSettingsProvider.Temperature)
            ? serviceSettings.GetRequired((FormNumberFieldBase<double>)Configuration.ServiceSettingsProvider.Temperature)
            : moduleTemperature;

        var maxNewTokens = serviceSettings.HasValue((FormNumberFieldBase<int>)Configuration.ServiceSettingsProvider.MaxNewTokens)
            ? serviceSettings.GetRequired((FormNumberFieldBase<int>)Configuration.ServiceSettingsProvider.MaxNewTokens)
            : moduleMaxNewTokens;

        var maxWindowTokens = serviceSettings.HasValue((FormNumberFieldBase<int>)Configuration.ServiceSettingsProvider.MaxWindowTokens)
            ? serviceSettings.GetRequired((FormNumberFieldBase<int>)Configuration.ServiceSettingsProvider.MaxWindowTokens)
            : moduleMaxWindowTokens;

        var maxMemoryTokens = serviceSettings.HasValue((FormNumberFieldBase<int>)Configuration.ServiceSettingsProvider.MaxMemoryTokens)
            ? serviceSettings.GetRequired((FormNumberFieldBase<int>)Configuration.ServiceSettingsProvider.MaxMemoryTokens)
            : moduleMaxMemoryTokens;

        var maxSummaryTokens = serviceSettings.HasValue((FormNumberFieldBase<int>)Configuration.ServiceSettingsProvider.MaxSummaryTokens)
            ? serviceSettings.GetRequired((FormNumberFieldBase<int>)Configuration.ServiceSettingsProvider.MaxSummaryTokens)
            : moduleMaxSummaryTokens;

        var summarizationDigestRatio = serviceSettings.HasValue((FormNumberFieldBase<double>)Configuration.ServiceSettingsProvider.SummarizationDigestRatio)
            ? serviceSettings.GetRequired((FormNumberFieldBase<double>)Configuration.ServiceSettingsProvider.SummarizationDigestRatio)
            : moduleSummarizationDigestRatio;

        var summarizationTriggerMessagesBuffer = serviceSettings.HasValue((FormNumberFieldBase<double>)Configuration.ServiceSettingsProvider.SummarizationTriggerMessagesBuffer)
            ? serviceSettings.GetRequired((FormNumberFieldBase<double>)Configuration.ServiceSettingsProvider.SummarizationTriggerMessagesBuffer)
            : moduleSummarizationTriggerMessagesBuffer;

        var keepLastMessages = serviceSettings.HasValue((FormNumberFieldBase<int>)Configuration.ServiceSettingsProvider.KeepLastMessages)
            ? serviceSettings.GetRequired((FormNumberFieldBase<int>)Configuration.ServiceSettingsProvider.KeepLastMessages)
            : moduleKeepLastMessages;

        var replySystemPromptPath = serviceSettings.HasValue(Configuration.ServiceSettingsProvider.ReplySystemPromptPath)
            ? serviceSettings.GetOptional(Configuration.ServiceSettingsProvider.ReplySystemPromptPath)
            : moduleReplySystemPromptPath;

        var summaryPromptPath = serviceSettings.HasValue(Configuration.ServiceSettingsProvider.SummaryPromptPath)
            ? serviceSettings.GetOptional(Configuration.ServiceSettingsProvider.SummaryPromptPath)
            : moduleSummaryPromptPath;

        var memoryExtractionPromptPath = serviceSettings.HasValue(Configuration.ServiceSettingsProvider.MemoryExtractionPromptPath)
            ? serviceSettings.GetOptional(Configuration.ServiceSettingsProvider.MemoryExtractionPromptPath)
            : moduleMemoryExtractionPromptPath;

        var enableGraphExtraction = serviceSettings.HasValue(Configuration.ServiceSettingsProvider.EnableGraphExtraction)
            ? serviceSettings.GetRequired(Configuration.ServiceSettingsProvider.EnableGraphExtraction)
            : moduleEnableGraphExtraction;

        var graphExtractionPromptPath = serviceSettings.HasValue(Configuration.ServiceSettingsProvider.GraphExtractionPromptPath)
            ? serviceSettings.GetOptional(Configuration.ServiceSettingsProvider.GraphExtractionPromptPath)
            : moduleGraphExtractionPromptPath;

        var logLifecycleEvents = serviceSettings.HasValue(Configuration.ServiceSettingsProvider.LogLifecycleEvents)
            ? serviceSettings.GetRequired(Configuration.ServiceSettingsProvider.LogLifecycleEvents)
            : moduleLogLifecycleEvents;

        return new YoloLlmSettings
        {
            ApiKey = apiKey,
            BaseUrl = moduleConfiguration.GetRequired(ModuleConfigurationProvider.BaseUrl),
            Model = model,
            Temperature = temperature,
            MaxNewTokens = maxNewTokens,
            MaxWindowTokens = maxWindowTokens,
            MaxMemoryTokens = maxMemoryTokens,
            MaxSummaryTokens = maxSummaryTokens,
            SummarizationDigestRatio = summarizationDigestRatio,
            SummarizationTriggerMessagesBuffer = summarizationTriggerMessagesBuffer,
            KeepLastMessages = keepLastMessages,
            ReplySystemPromptPath = replySystemPromptPath,
            SummaryPromptPath = summaryPromptPath,
            MemoryExtractionPromptPath = memoryExtractionPromptPath,
            EnableGraphExtraction = enableGraphExtraction,
            GraphExtractionPromptPath = graphExtractionPromptPath,
            LogLifecycleEvents = logLifecycleEvents,
        };
    }

    private static string TryDecrypt(ILocalEncryptionProvider localEncryptionProvider, string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        try
        {
            return localEncryptionProvider.Decrypt(value);
        }
        catch
        {
            return value;
        }
    }
}
