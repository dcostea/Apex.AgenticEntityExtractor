using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace Apex.AgenticEntityExtractor.Builders;

public class ExtractorChatClientBuilder(IConfiguration configuration) : IExtractorChatClientBuilder
{
    public IChatClient BuildOllamaChatClient()
    {
        string model = "hf.co/unsloth/Mistral-Small-3.1-24B-Instruct-2503-GGUF:Q4_K_M";
        string ollamaServer = "http://localhost:11434";

        /*
        hf.co/bartowski/mistralai_Mistral-Small-3.1-24B-Instruct-2503-GGUF:Q4_K_M   // inconstant
        hf.co/unsloth/Mistral-Small-3.1-24B-Instruct-2503-GGUF:Q4_K_M               // good!
        hf.co/unsloth/Mistral-Small-3.2-24B-Instruct-2506-GGUF:Q4_K_M               // good!
         */

        var chatClient = new OllamaApiClient(ollamaServer, model);

        var modelInfo = chatClient.ShowModelAsync(model).Result;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"\nMODEL: {model}");

        string[] capabilities = ["completion", "tools", "vision"];
        foreach (var capability in capabilities)
        {
            bool hasCapability = modelInfo.Capabilities!.Contains(capability);
            Console.ForegroundColor = hasCapability ? ConsoleColor.White : ConsoleColor.Red;
            Console.WriteLine($"[{(hasCapability ? "✓" : "✗")}] {capability}");
        }
        Console.ResetColor();

        return chatClient;
    }

    public IChatClient BuildOpenAIChatClient()
    {
        var model = configuration["OpenAI:ModelId"]!;
        var apiKey = configuration["OpenAI:ApiKey"]!;

        Console.WriteLine($"\nMODEL: {model}");
        
        var chatClient = new OpenAIClient(apiKey)
            .GetChatClient(model)
            .AsIChatClient();

        return chatClient;
    }
}
