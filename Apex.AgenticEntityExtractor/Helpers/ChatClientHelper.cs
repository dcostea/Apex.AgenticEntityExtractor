using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using System.ClientModel;

namespace Apex.AgenticEntityExtractor.Helpers;

public static class ChatClientHelper
{
    public static async Task<IChatClient> BuildOpenAIChatClientAsync()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
        var endpoint = configuration["AzureOpenAI:Endpoint"]!;
        var apiKey = configuration["AzureOpenAI:ApiKey"]!;
        var deploymentName = configuration["AzureOpenAI:DeploymentName"]!;

        Console.WriteLine($"Model: {deploymentName}");
        Console.WriteLine();

        var chatClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
            .GetChatClient(deploymentName)
            .AsIChatClient();

        return chatClient;
    }

    public static async Task<IChatClient> BuildOllamaChatClientAsync()
    {
        string model = "hf.co/unsloth/Mistral-Small-3.1-24B-Instruct-2503-GGUF:Q4_K_M";
        string OllamaServer = "http://localhost:11434";

        /*
        hf.co/bartowski/mistralai_Mistral-Small-3.1-24B-Instruct-2503-GGUF:Q4_K_M   // inconstant
        hf.co/unsloth/Mistral-Small-3.1-24B-Instruct-2503-GGUF:Q4_K_M               // good!
        hf.co/unsloth/Mistral-Small-3.2-24B-Instruct-2506-GGUF:Q4_K_M               // good!
         */

        var chatClient = new OllamaApiClient(OllamaServer, model);
        var modelInfo = await chatClient.ShowModelAsync(model);
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"Model: {model}");

        string[] capabilities = ["completion", "tools", "vision"];
        foreach (var capability in capabilities)
        {
            bool hasCapability = modelInfo.Capabilities!.Contains(capability);
            Console.ForegroundColor = hasCapability ? ConsoleColor.White : ConsoleColor.Red;
            Console.WriteLine($"[{(hasCapability ? "✓" : "✗")}] {capability}");
        }
        Console.ResetColor();
        Console.WriteLine();

        return chatClient;
    }
}
