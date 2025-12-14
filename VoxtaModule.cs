using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Voxta.Abstractions.Modules;
using Voxta.Abstractions.Registration;
using Voxta.Model.Shared;
using Voxta.Modules.YoloLLM.Configuration;
using Voxta.Modules.YoloLLM.Services;

namespace Voxta.Modules.YoloLLM;

[UsedImplicitly]
public class VoxtaModule : IVoxtaModule
{
    public const string ServiceName = "YoloLLM";

    public void Configure(IVoxtaModuleBuilder builder)
    {
        builder.Register(new ModuleDefinition
        {
            ServiceName = ServiceName,
            Label = "YOLO LLM (TextGen + Summarization)",
            Notes = "OpenAI-compatible client that brings its own LLM for replies and summaries. Experimental / BYO endpoint.",
            Supports = new()
            {
                { ServiceTypes.TextGen, ServiceDefinitionCategoryScore.High },
                { ServiceTypes.Summarization, ServiceDefinitionCategoryScore.Medium },
            },
            Pricing = ServiceDefinitionPricing.Medium,
            Hosting = ServiceDefinitionHosting.Online,
            Experimental = true,
            CanBeInstalledByAdminsOnly = false,
            SupportsExplicitContent = true,
            Recommended = false,
            ModuleConfigurationProviderType = typeof(ModuleConfigurationProvider),
            ModuleConfigurationFieldsRequiringReload = ModuleConfigurationProvider.FieldsRequiringReload,
            PresetsProviderType = typeof(ServiceSettingsProvider),
            PresetsFieldsRequiringReload = ServiceSettingsProvider.FieldsRequiringReload,
        });

        builder.AddTextGenService<YoloTextGenService>(ServiceName);
        builder.AddSummarizationService<YoloSummarizationService>(ServiceName);

        builder.Services.AddHttpClient();
    }
}
