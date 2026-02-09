// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using UtilityBillingChatbot.Models;

namespace UtilityBillingChatbot.Hosting;

/// <summary>
/// Factory for creating chat clients based on LLM configuration.
/// </summary>
public static class ChatClientFactory
{
    /// <summary>
    /// Creates a chat client based on the configured LLM provider.
    /// </summary>
    public static IChatClient Create(LlmOptions options)
    {
        return options.Provider switch
        {
            "AzureOpenAI" when options.AzureOpenAI is { Endpoint: not null, ApiKey: not null } =>
                CreateAzureOpenAI(options.AzureOpenAI),

            "OpenAI" when options.OpenAI is { ApiKey: not null } =>
                CreateOpenAI(options.OpenAI),

            "HuggingFace" when options.HuggingFace is { ApiKey: not null } =>
                CreateHuggingFace(options.HuggingFace),

            _ => throw new InvalidOperationException($"Invalid provider configuration: {options.Provider}")
        };
    }

    private static IChatClient CreateAzureOpenAI(AzureOpenAIOptions options)
    {
        var client = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(options.Endpoint!),
            new System.ClientModel.ApiKeyCredential(options.ApiKey!));
        return client.GetChatClient(options.DeploymentName).AsIChatClient();
    }

    private static IChatClient CreateOpenAI(OpenAIOptions options)
    {
        OpenAI.OpenAIClient client;
        if (!string.IsNullOrEmpty(options.Endpoint))
        {
            var clientOptions = new OpenAI.OpenAIClientOptions
            {
                Endpoint = new Uri(options.Endpoint)
            };
            client = new OpenAI.OpenAIClient(
                new System.ClientModel.ApiKeyCredential(options.ApiKey!),
                clientOptions);
        }
        else
        {
            client = new OpenAI.OpenAIClient(options.ApiKey!);
        }

        return client.GetChatClient(options.Model).AsIChatClient();
    }

    private static IChatClient CreateHuggingFace(HuggingFaceOptions options)
    {
        var endpointStr = options.Endpoint;
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
            new System.ClientModel.ApiKeyCredential(options.ApiKey!),
            clientOptions);

        return client.GetChatClient(options.Model).AsIChatClient();
    }
}
