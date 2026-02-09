// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates Stage 1 of the Utility Billing Customer Support Chatbot:
// A Classifier Agent that categorizes customer questions into appropriate routing categories.

using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UtilityBillingChatbot.Models;
using UtilityBillingChatbot.MultiAgent;

// Load configuration from appsettings.json and environment variables
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

// Load LLM options
var llmOptions = configuration.GetSection("LLM").Get<LlmOptions>()
    ?? throw new InvalidOperationException("LLM configuration not found in appsettings.json");

// Fallback: check common HuggingFace environment variable names if ApiKey not set
if (llmOptions.HuggingFace is not null && string.IsNullOrEmpty(llmOptions.HuggingFace.ApiKey))
{
    llmOptions.HuggingFace.ApiKey =
        Environment.GetEnvironmentVariable("HF_TOKEN") ??
        Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY") ??
        Environment.GetEnvironmentVariable("HUGGINGFACE_TOKEN");
}

// Load verified questions from JSON file
var verifiedQuestionsPath = configuration["VerifiedQuestionsPath"] ?? "Data/verified-questions.json";
if (!Path.IsPathRooted(verifiedQuestionsPath))
{
    verifiedQuestionsPath = Path.Combine(AppContext.BaseDirectory, verifiedQuestionsPath);
}

var verifiedQuestionsJson = await File.ReadAllTextAsync(verifiedQuestionsPath);
var verifiedQuestions = JsonSerializer.Deserialize<List<VerifiedQuestion>>(verifiedQuestionsJson, JsonSerializerOptions.Web)
    ?? throw new InvalidOperationException("Failed to load verified questions");

Console.WriteLine($"Loaded {verifiedQuestions.Count} verified question types.");
Console.WriteLine();

// Configure services using DI
var services = new ServiceCollection();

// Register the chat client
var chatClient = CreateChatClient(llmOptions);
services.AddSingleton<IChatClient>(chatClient);

// Register multi-agent services (agent definitions and registry)
services.AddMultiAgentServices(verifiedQuestions);

// Build service provider and initialize agents
var serviceProvider = services.BuildServiceProvider();
var agentRegistry = serviceProvider.BuildAgentRegistry();

// Get the classifier agent from the registry
var classifierAgent = agentRegistry.GetAgent("classifier");

Console.WriteLine("=== Utility Billing Customer Support Classifier ===");
Console.WriteLine("Enter your question (or 'quit' to exit):");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine()?.Trim();

    if (string.IsNullOrEmpty(input))
    {
        continue;
    }

    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Goodbye!");
        break;
    }

    try
    {
        // Use structured output to get the classification
        var response = await classifierAgent.RunAsync<QuestionClassification>(input);
        var classification = response.Result;

        Console.WriteLine();
        Console.WriteLine("Classification Result:");
        Console.WriteLine($"  Category:     {classification.Category}");
        Console.WriteLine($"  QuestionType: {classification.QuestionType ?? "(none)"}");
        Console.WriteLine($"  Confidence:   {classification.Confidence:F2}");
        Console.WriteLine($"  RequiresAuth: {classification.RequiresAuth}");
        Console.WriteLine($"  Reasoning:    {classification.Reasoning}");

        // Show matching verified question if found
        if (!string.IsNullOrEmpty(classification.QuestionType))
        {
            var matchedQuestion = verifiedQuestions.FirstOrDefault(q =>
                q.Id.Equals(classification.QuestionType, StringComparison.OrdinalIgnoreCase));

            if (matchedQuestion != null)
            {
                Console.WriteLine();
                Console.WriteLine("Matched Verified Question:");
                Console.WriteLine($"  ID:          {matchedQuestion.Id}");
                Console.WriteLine($"  Description: {matchedQuestion.Description}");
                Console.WriteLine($"  AuthLevel:   {matchedQuestion.RequiredAuthLevel}");
                Console.WriteLine($"  Plugins:     {string.Join(", ", matchedQuestion.RequiredPlugins)}");
            }
        }

        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        Console.WriteLine();
    }
}

// Helper method to create chat client from configuration
static IChatClient CreateChatClient(LlmOptions options)
{
    return options.Provider switch
    {
        "AzureOpenAI" when options.AzureOpenAI is { Endpoint: not null, ApiKey: not null } =>
            CreateAzureOpenAIChatClient(options.AzureOpenAI),

        "OpenAI" when options.OpenAI is { ApiKey: not null } =>
            CreateOpenAIChatClient(options.OpenAI),

        "HuggingFace" when options.HuggingFace is { ApiKey: not null } =>
            CreateHuggingFaceChatClient(options.HuggingFace),

        _ => throw new InvalidOperationException($"Invalid provider configuration: {options.Provider}")
    };
}

static IChatClient CreateAzureOpenAIChatClient(AzureOpenAIOptions options)
{
    Console.WriteLine($"Using Azure OpenAI: {options.Endpoint}");
    var client = new Azure.AI.OpenAI.AzureOpenAIClient(
        new Uri(options.Endpoint!),
        new System.ClientModel.ApiKeyCredential(options.ApiKey!));
    return client.GetChatClient(options.DeploymentName).AsIChatClient();
}

static IChatClient CreateOpenAIChatClient(OpenAIOptions options)
{
    Console.WriteLine($"Using OpenAI: {options.Model}");
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

static IChatClient CreateHuggingFaceChatClient(HuggingFaceOptions options)
{
    Console.WriteLine($"Using HuggingFace: {options.Model}");

    var endpointStr = options.Endpoint;
    if (!endpointStr.EndsWith("/v1/", StringComparison.Ordinal) && !endpointStr.EndsWith("/v1", StringComparison.Ordinal))
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
