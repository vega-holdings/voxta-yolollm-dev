using Voxta.Abstractions.Registration;
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
    public bool LogLifecycleEvents { get; init; }
}

internal static class YoloLlmSettingsLoader
{
    public static YoloLlmSettings Load(ISettingsSource moduleConfiguration)
    {
        return new YoloLlmSettings
        {
            ApiKey = moduleConfiguration.GetRequired(ModuleConfigurationProvider.ApiKey),
            BaseUrl = moduleConfiguration.GetRequired(ModuleConfigurationProvider.BaseUrl),
            Model = moduleConfiguration.GetRequired(ModuleConfigurationProvider.Model),
            Temperature = (double)(moduleConfiguration.GetOptional((FormNumberFieldBase<double>)ModuleConfigurationProvider.Temperature)
                ?? ModuleConfigurationProvider.Temperature.DefaultValue
                ?? 0.7),
            MaxNewTokens = moduleConfiguration.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxNewTokens),
            MaxWindowTokens = moduleConfiguration.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxWindowTokens),
            MaxMemoryTokens = moduleConfiguration.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxMemoryTokens),
            MaxSummaryTokens = moduleConfiguration.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.MaxSummaryTokens),
            SummarizationDigestRatio = moduleConfiguration.GetRequired((FormNumberFieldBase<double>)ModuleConfigurationProvider.SummarizationDigestRatio),
            SummarizationTriggerMessagesBuffer = moduleConfiguration.GetRequired((FormNumberFieldBase<double>)ModuleConfigurationProvider.SummarizationTriggerMessagesBuffer),
            KeepLastMessages = moduleConfiguration.GetRequired((FormNumberFieldBase<int>)ModuleConfigurationProvider.KeepLastMessages),
            ReplySystemPromptPath = moduleConfiguration.GetOptional(ModuleConfigurationProvider.ReplySystemPromptPath),
            SummaryPromptPath = moduleConfiguration.GetOptional(ModuleConfigurationProvider.SummaryPromptPath),
            MemoryExtractionPromptPath = moduleConfiguration.GetOptional(ModuleConfigurationProvider.MemoryExtractionPromptPath),
            LogLifecycleEvents = moduleConfiguration.GetRequired(ModuleConfigurationProvider.LogLifecycleEvents),
        };
    }
}
