using Apex.AgenticEntityExtractor.Helpers;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using System.ClientModel;

namespace Apex.AgenticEntityExtractor.Clients;

public class ExtractorChatClientBuilder(IConfiguration configuration) : IExtractorChatClientBuilder
{
    public IChatClient BuildOllamaChatClient()
    {
        string model = configuration["Ollama:Model"]!;
        string ollamaServer = configuration["Ollama:Server"]!;

        var chatClient = new OllamaApiClient(ollamaServer, model);

        var modelInfo = chatClient.ShowModelAsync(model).Result;
        ConsoleHelper.PrintColoredLine($"\nMODEL: {model}", ConsoleColor.Yellow);

        string[] capabilities = ["completion", "tools", "vision"];
        foreach (var capability in capabilities)
        {
            bool hasCapability = modelInfo.Capabilities!.Contains(capability);
            ConsoleHelper.PrintColoredLine($"{(hasCapability ? "✓" : "✗")} {capability}", hasCapability ? ConsoleColor.White : ConsoleColor.Red);
        }
        Console.ResetColor();

        return chatClient;
    }

    public IChatClient BuildOpenAIChatClient()
    {
        var model = configuration["OpenAI:ModelId"]!;
        var apiKey = configuration["OpenAI:ApiKey"]!;

        ConsoleHelper.PrintColoredLine($"\nMODEL: {model}", ConsoleColor.Yellow);
        
        var chatClient = new OpenAIClient(apiKey)
            .GetChatClient(model)
            .AsIChatClient();

        return chatClient;
    }

    public IChatClient BuildAzureOpenAIChatClient()
    {
        var endpoint = configuration["AzureOpenAI:Endpoint"]!;
        var apiKey = configuration["AzureOpenAI:ApiKey"]!;
        var deploymentName = configuration["AzureOpenAI:DeploymentName"]!;

        ConsoleHelper.PrintColoredLine($"\nMODEL: {deploymentName}", ConsoleColor.Yellow);

        IChatClient chatClient = new AzureOpenAIClient(
                new Uri(endpoint),
                new ApiKeyCredential(apiKey))
            .GetChatClient(deploymentName)
            .AsIChatClient();

        return chatClient;
    }
}
