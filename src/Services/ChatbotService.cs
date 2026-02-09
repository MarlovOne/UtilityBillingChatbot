// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UtilityBillingChatbot.Agents;
using UtilityBillingChatbot.Models;
using UtilityBillingChatbot.Telemetry;

namespace UtilityBillingChatbot.Services;

/// <summary>
/// Background service that runs the interactive chatbot console.
/// </summary>
public class ChatbotService : BackgroundService
{
    private readonly ClassifierAgent _classifierAgent;
    private readonly IReadOnlyList<VerifiedQuestion> _verifiedQuestions;
    private readonly AgentMetrics _metrics;
    private readonly LlmOptions _llmOptions;
    private readonly ILogger<ChatbotService> _logger;

    public ChatbotService(
        ClassifierAgent classifierAgent,
        IReadOnlyList<VerifiedQuestion> verifiedQuestions,
        AgentMetrics metrics,
        IOptions<LlmOptions> llmOptions,
        ILogger<ChatbotService> logger)
    {
        _classifierAgent = classifierAgent;
        _verifiedQuestions = verifiedQuestions;
        _metrics = metrics;
        _llmOptions = llmOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PrintStartupInfo();

        Console.WriteLine("=== Utility Billing Customer Support Classifier ===");
        Console.WriteLine("Enter your question (or 'quit' to exit):");
        Console.WriteLine();

        while (!stoppingToken.IsCancellationRequested)
        {
            Console.Write("> ");

            // Use a task to allow cancellation during ReadLine
            var inputTask = Task.Run(() => Console.ReadLine()?.Trim(), stoppingToken);

            try
            {
                var input = await inputTask;

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

                await HandleUserInputAsync(input, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
        }

        _logger.LogInformation("Chatbot session ended");
    }

    private void PrintStartupInfo()
    {
        Console.WriteLine($"Loaded {_verifiedQuestions.Count} verified question types.");
        Console.WriteLine($"Using {_llmOptions.Provider}: {GetModelName()}");
        Console.WriteLine();
    }

    private string GetModelName() => _llmOptions.Provider switch
    {
        "AzureOpenAI" => _llmOptions.AzureOpenAI?.DeploymentName ?? "unknown",
        "OpenAI" => _llmOptions.OpenAI?.Model ?? "unknown",
        "HuggingFace" => _llmOptions.HuggingFace?.Model ?? "unknown",
        _ => "unknown"
    };

    private async Task HandleUserInputAsync(string input, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("User input received: {Length} chars", input.Length);

            var result = await _classifierAgent.ClassifyAsync(input, cancellationToken);

            if (!result.IsSuccess)
            {
                Console.WriteLine();
                Console.WriteLine("Unable to classify the question. " + result.Error);
                Console.WriteLine();
                return;
            }

            var classification = result.Classification!;

            // Record classification metric
            _metrics.ClassificationResults.Add(1,
                new KeyValuePair<string, object?>("category", classification.Category));

            PrintClassificationResult(classification);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user input: {Error}", ex.Message);
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine();
        }
    }

    private void PrintClassificationResult(QuestionClassification classification)
    {
        Console.WriteLine();
        Console.WriteLine("Classification Result:");
        Console.WriteLine($"  Category:     {classification.Category}");
        Console.WriteLine($"  QuestionType: {classification.QuestionType ?? "(none)"}");
        Console.WriteLine($"  Confidence:   {classification.Confidence:F2}");
        Console.WriteLine($"  RequiresAuth: {classification.RequiresAuth}");
        Console.WriteLine($"  Reasoning:    {classification.Reasoning}");

        // Show matching verified question if found
        var matchedQuestion = _classifierAgent.FindMatchingQuestion(classification);
        if (matchedQuestion is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Matched Verified Question:");
            Console.WriteLine($"  ID:          {matchedQuestion.Id}");
            Console.WriteLine($"  Description: {matchedQuestion.Description}");
            Console.WriteLine($"  AuthLevel:   {matchedQuestion.RequiredAuthLevel}");
            Console.WriteLine($"  Plugins:     {string.Join(", ", matchedQuestion.RequiredPlugins)}");
        }

        Console.WriteLine();
    }
}
