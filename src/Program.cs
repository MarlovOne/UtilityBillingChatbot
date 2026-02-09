// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates Stage 1 of the Utility Billing Customer Support Chatbot:
// A Classifier Agent that categorizes customer questions into appropriate routing categories.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Extensions;
using UtilityBillingChatbot.Hosting;
using UtilityBillingChatbot.Models;
using UtilityBillingChatbot.Telemetry;

// Build the chatbot host with all services configured
var builder = new ChatbotHostBuilder();
using var host = await builder.BuildAsync();

Console.WriteLine($"Loaded {builder.VerifiedQuestions.Count} verified question types.");
Console.WriteLine($"Using {builder.LlmOptions.Provider}: {GetModelName(builder.LlmOptions)}");
Console.WriteLine();

// Get logger for Program
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
logger.LogInformation("Starting chatbot with {AgentCount} registered agents", host.AgentRegistry.GetAll().Count);

// Get the classifier agent from the registry
// Use GetBaseAgent for structured output support (RunAsync<T>)
var classifierAgent = host.AgentRegistry.GetBaseAgent("classifier");

static string GetModelName(LlmOptions options) => options.Provider switch
{
    "AzureOpenAI" => options.AzureOpenAI?.DeploymentName ?? "unknown",
    "OpenAI" => options.OpenAI?.Model ?? "unknown",
    "HuggingFace" => options.HuggingFace?.Model ?? "unknown",
    _ => "unknown"
};

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
        logger.LogInformation("User input received: {Length} chars", input.Length);

        // Use structured output to get the classification
        var response = await classifierAgent.RunAsync<QuestionClassification>(input);

        // Try to get the structured result - handle JSON parsing failures gracefully
        if (!response.TryGetResult(out var classification, out var parseError))
        {
            logger.LogWarning("Failed to parse classifier response: {Error}", parseError);
            Console.WriteLine();
            Console.WriteLine("Unable to classify the question. " + parseError);
            Console.WriteLine();
            continue;
        }

        logger.LogInformation("Classification: {Category}, Confidence: {Confidence:F2}",
            classification.Category, classification.Confidence);

        // Record classification metric
        var metrics = host.Services.GetRequiredService<AgentMetrics>();
        metrics.ClassificationResults.Add(1,
            new KeyValuePair<string, object?>("category", classification.Category));

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
            var matchedQuestion = builder.VerifiedQuestions.FirstOrDefault(q =>
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
        logger.LogError(ex, "Error processing user input: {Error}", ex.Message);
        Console.WriteLine($"Error: {ex.Message}");
        Console.WriteLine();
    }
}

logger.LogInformation("Chatbot session ended");
