using Autofac;
using McpServer.Application.Abstractions.Inference;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Mcp.Tools.Inference;
using McpServer.Host.Configuration;
using McpServer.Infrastructure.Ollama;

namespace McpServer.Host.DependencyInjection;

public sealed class OllamaFeatureModule(OllamaOptions options) : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.Register(_ => new OllamaInferenceOptions(
                enabled: options.Enabled,
                baseUrl: options.BaseUrl,
                defaultModel: options.DefaultModel,
                allowedModels: options.AllowedModels,
                timeoutSeconds: options.TimeoutSeconds,
                maxTimeoutSeconds: options.MaxTimeoutSeconds,
                maxPromptChars: options.MaxPromptChars,
                maxOutputChars: options.MaxOutputChars,
                contextLength: options.ContextLength,
                numPredict: options.NumPredict,
                temperature: options.Temperature,
                allowNonLoopbackBaseUrl: options.AllowNonLoopbackBaseUrl))
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OllamaInferenceService>()
            .As<ILocalInferenceService>()
            .SingleInstance();

        RegisterTool<LocalInferenceStatusToolHandler>(builder);
        RegisterTool<LocalCompleteToolHandler>(builder);
        RegisterTool<LocalSummarizeToolHandler>(builder);
        RegisterTool<LocalCodeReviewToolHandler>(builder);
        RegisterTool<LocalPlanToolHandler>(builder);
    }

    private static void RegisterTool<TToolHandler>(ContainerBuilder builder)
        where TToolHandler : class, IToolHandler
    {
        builder.RegisterType<TToolHandler>()
            .AsSelf()
            .As<IToolHandler>()
            .SingleInstance();
    }
}
