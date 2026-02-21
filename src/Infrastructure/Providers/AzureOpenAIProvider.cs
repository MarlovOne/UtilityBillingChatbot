// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace UtilityBillingChatbot.Infrastructure.Providers;

public class AzureOpenAIProvider(IConfiguration configuration) : LlmProviderBase<AzureOpenAIOptions>(configuration)
{
    public override string Name => "AzureOpenAI";

    public override string ModelDisplayName => Options.DeploymentName;

    public override IChatClient CreateClient()
    {
        if (string.IsNullOrEmpty(Options.Endpoint))
            throw new InvalidOperationException("AzureOpenAI Endpoint is required.");
        if (string.IsNullOrEmpty(Options.ApiKey))
            throw new InvalidOperationException("AzureOpenAI ApiKey is required.");

        var client = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(Options.Endpoint),
            new System.ClientModel.ApiKeyCredential(Options.ApiKey));

        return client.GetChatClient(Options.DeploymentName).AsIChatClient();
    }
}
