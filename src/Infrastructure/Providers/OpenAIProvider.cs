// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace UtilityBillingChatbot.Infrastructure.Providers;

public class OpenAIProvider(IConfiguration configuration) : LlmProviderBase<OpenAIOptions>(configuration)
{
    public override string Name => "OpenAI";

    public override string ModelDisplayName => Options.Model;

    public override IChatClient CreateClient()
    {
        if (string.IsNullOrEmpty(Options.ApiKey))
            throw new InvalidOperationException("OpenAI ApiKey is required.");

        OpenAI.OpenAIClient client;
        if (!string.IsNullOrEmpty(Options.Endpoint))
        {
            var clientOptions = new OpenAI.OpenAIClientOptions
            {
                Endpoint = new Uri(Options.Endpoint)
            };
            client = new OpenAI.OpenAIClient(
                new System.ClientModel.ApiKeyCredential(Options.ApiKey),
                clientOptions);
        }
        else
        {
            client = new OpenAI.OpenAIClient(Options.ApiKey);
        }

        return client.GetChatClient(Options.Model).AsIChatClient();
    }
}
