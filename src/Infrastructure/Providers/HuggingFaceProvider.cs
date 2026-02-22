// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace UtilityBillingChatbot.Infrastructure.Providers;

public class HuggingFaceProvider(IConfiguration configuration) : ILlmProvider
{
    public string Name => "HuggingFace";
    public LlmProviderOptions Options { get; } = ILlmProvider.BindOptions(configuration, "HuggingFace");

    public IChatClient CreateClient(string model)
    {
        // Env var fallback for API key
        if (string.IsNullOrEmpty(Options.ApiKey))
        {
            Options.ApiKey =
                Environment.GetEnvironmentVariable("HF_TOKEN") ??
                Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY") ??
                Environment.GetEnvironmentVariable("HUGGINGFACE_TOKEN");
        }

        if (string.IsNullOrEmpty(Options.ApiKey))
            throw new InvalidOperationException("HuggingFace ApiKey is required (config or HF_TOKEN env var).");

        var endpointStr = Options.Endpoint ?? "https://api-inference.huggingface.co/v1/";
        if (!endpointStr.EndsWith("/v1/", StringComparison.Ordinal) &&
            !endpointStr.EndsWith("/v1", StringComparison.Ordinal))
        {
            endpointStr = endpointStr.TrimEnd('/') + "/v1/";
        }

        var clientOptions = new OpenAI.OpenAIClientOptions
        {
            Endpoint = new Uri(endpointStr)
        };

        var client = new OpenAI.OpenAIClient(
            new System.ClientModel.ApiKeyCredential(Options.ApiKey),
            clientOptions);

        return client.GetChatClient(model).AsIChatClient();
    }
}
