using AgenticEntityExtractor.Helpers;
using AgenticEntityExtractor.Tools;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using OpenAI;
using System.ClientModel;

static async Task<IChatClient> CreateOpenAIChatClientAsync()
{
    var configuration = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
    var endpoint = configuration["AzureOpenAI:Endpoint"]!;
    var apiKey = configuration["AzureOpenAI:ApiKey"]!;
    var deploymentName = configuration["AzureOpenAI:DeploymentName"]!;

    Console.WriteLine($"{deploymentName}");

    var chatClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
        .GetChatClient(deploymentName)
        .AsIChatClient();

    return chatClient;
}

static async Task<IChatClient> CreateOllamaChatClientAsync()
{
    string model = "Mistral-Small-3.1-24B-Instruct-2503-Q4_K_M";
    string OllamaServer = "http://localhost:11434";

    /*
    Mistral-Small-3.1-24B-Instruct-2503-Q3_K_XL // fast but not very good (45s)
    Mistral-Small-3.1-24B-Instruct-2503-Q4_K_S // fast sometimes misses (47s)
    Mistral-Small-3.1-24B-Instruct-2503-Q4_K_M // fast sometimes breaks mermaid (51s)
    Mistral-Small-3.2-24B-Instruct-2506-Q3_K_XL // fast but breaks tools (25s)
    Mistral-Small-3.2-24B-Instruct-2506-Q4_K_M // fast but breaks tools (23s)
     */

    var chatClient = new OllamaApiClient(OllamaServer, model);
    var modelInfo = await chatClient.ShowModelAsync(model);
    Console.WriteLine($"{model} [{string.Join(", ", modelInfo.Capabilities!)}]");
    return chatClient;
}

// Select one of the chat clients (Ollama or OpenAI)
//var chatClient = await CreateOllamaChatClientAsync();
var chatClient = await CreateOpenAIChatClientAsync();

var entitiesAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
{
    Name = "EntitiesAgent",
    Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "EntitiesAgent.md")),
    ChatOptions = new ChatOptions
    {
        MaxOutputTokens = 1000,
        Temperature = 0.1F,
        Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadEntitiesOntology), AIFunctionFactory.Create(FilesPlugin.SaveEntities)],
        //ToolMode = ChatToolMode.Auto,
    }
});

var relationshipsAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
{
    Name = "RelationshipsAgent",
    Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "RelationshipsAgent.md")),
    ChatOptions = new ChatOptions
    {
        MaxOutputTokens = 1000,
        Temperature = 0.4F,
        Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadRelationshipsOntology), AIFunctionFactory.Create(FilesPlugin.SaveRelationships)],
        //ToolMode = ChatToolMode.Auto,
    }
});

var diagramBuilderAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
{
    Name = "DiagramBuilderAgent",
    Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "DiagramBuilderAgent.md")),
    ChatOptions = new ChatOptions
    {
        MaxOutputTokens = 1000,
        Temperature = 0.1F,
        //Tools = [AIFunctionFactory.Create(FilesPlugin.SaveMermaidGraph)],
        //ToolMode = ChatToolMode.RequireSpecific(nameof(FilesPlugin.SaveMermaidGraph)),
    }
});

var input = File.ReadAllText(Path.Combine("Data", "Input", "input.txt"));
var query = $"""
    ## CONTEXT
    Input:
    ```
    {input}
    ```
    """;

WorkflowHelper.PrintColoredLine($"""
    QUERY:

    {query}
    """, ConsoleColor.Green);

var workflow = AgentWorkflowBuilder.BuildSequential(entitiesAgent, relationshipsAgent, diagramBuilderAgent);

// set this flag on false for running only part 1 of the demo, which shows the sequential workflow execution (with events)
// set this flag on true for running part 2 of the demo, which shows the group chat workflow execution
var runDemoPart2 = true;

if (!runDemoPart2)
{
    // In this demo (part 1) we are going to run a sequential workflow that extracts entities and relationships,
    // and then builds a mermaid diagram
    Console.WriteLine("DEMO - PART 1");

    await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, input: query);
    await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
    await WorkflowHelper.PrintWorkflowExecutionEventsAsync(run);
}
else
{
    // In this demo (part 2) we are going to declare two additional agents that will validate and correct diagrams
    Console.WriteLine("DEMO - PART 2");

    // is convenient to convert the workflow to an agent for easier getting the final result,
    // but we could also stream the execution as in part 1 and capture the output result
    var workflowExtractionAgent = workflow.AsAgent();
    var result = await workflowExtractionAgent.RunAsync(query);
    await WorkflowHelper.PrintAgentResponseStreamAsync(query, workflowExtractionAgent);

    WorkflowHelper.PrintColoredLine($"""
    RESULT:
    {result.Text}
    """, ConsoleColor.Green);

    var diagramCorrectorAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
    {
        Name = "DiagramCorrectorAgent",
        Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "DiagramCorrectorAgent.md")),
        ChatOptions = new ChatOptions
        {
            MaxOutputTokens = 1000,
            Temperature = 0.1F,
        }
    });

    var validationAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
    {
        Name = "ValidationAgent",
        Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "ValidationAgent.md")),
        ChatOptions = new ChatOptions
        {
            MaxOutputTokens = 1000,
            Temperature = 0.1F,
        }
    });

    var validationWorkflow = AgentWorkflowBuilder
        .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents) { MaximumIterationCount = 7 })
        .AddParticipants(validationAgent, diagramCorrectorAgent)
        .Build();

    var mermaid = validationWorkflow.ToMermaidString();
    Console.WriteLine($"Validation Workflow Mermaid Diagram: {mermaid}");

    var followUpQuery = $"""
    ## TASK
    Validate the following Mermaid diagram and respond with the final corrected diagram:

    ## INPUT
    {result.Text}
    """;

    StreamingRun followUpRun = await InProcessExecution.StreamAsync(validationWorkflow, input: followUpQuery);
    await followUpRun.TrySendMessageAsync(new TurnToken(emitEvents: true));
    await WorkflowHelper.PrintWorkflowExecutionEventsAsync(followUpRun);
}
