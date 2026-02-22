// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace UtilityBillingChatbot.Infrastructure.Providers;

public class OpenAIProvider(IConfiguration configuration) : ILlmProvider
{
    public string Name => "OpenAI";
    public LlmProviderOptions Options { get; } = ILlmProvider.BindOptions(configuration, "OpenAI");

    public IChatClient CreateClient(string model)
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

        return client.GetChatClient(model).AsIChatClient();
    }
}
