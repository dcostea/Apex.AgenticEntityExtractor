//using Apex.AgenticEntityExtractor.Helpers;
//using Microsoft.Agents.AI;
//using Microsoft.Agents.AI.Workflows;
//using Microsoft.Extensions.AI;

//// Select configuration for agent execution
//bool UseConcurrentEntitiesAgents = true; // true means running 3 entity agents concurrently
//bool UseConcurrentRelationshipsAgents = true; // true means running 3 relationship agents concurrently
//bool useGroupChatAgents = true; // true means using group chat for mermaid diagram generation

//// Select one of the chat clients (Ollama or OpenAI)
//bool useSelfHostedOllama = false; // Set to true to use Ollama, false to use OpenAI
//IChatClient chatClient = useSelfHostedOllama
//    ? await ChatClientHelper.BuildOllamaChatClientAsync()
//    : await ChatClientHelper.BuildOpenAIChatClientAsync();

//// Run concurrent or single agents for each step
//AIAgent entitiesAgent;
//ExecutorBinding? entitiesTripleAgentSubWorkflow = null;
//if (UseConcurrentEntitiesAgents)
//{
//    Console.WriteLine("Using Concurrent Entity Agents");
//    var entitiesAgent1 = AgentHelper.BuildEntitiesAgent(chatClient);
//    var entitiesAgent2 = AgentHelper.BuildEntitiesAgent(chatClient);
//    var entitiesAgent3 = AgentHelper.BuildEntitiesAgent(chatClient);
//    var entitiesWorkflow = AgentWorkflowBuilder.BuildConcurrent(
//        [entitiesAgent1, entitiesAgent2, entitiesAgent3],
//        WorkflowHelper.AggregateEntities);
//    ////entitiesAgent = entitiesWorkflow.AsAgent(name: "EntitiesTripleAgent", 
//    ////    description: "Aggregates the result from Entity Agents");
//    entitiesTripleAgentSubWorkflow = entitiesWorkflow.BindAsExecutor("EntitiesTripleAgentSubWorkflow");
//}
//else
//{
//    Console.WriteLine("Using Single Entity Agent");
//    entitiesAgent = AgentHelper.BuildEntitiesAgent(chatClient);
//}

//// Run concurrent or single agents for each step
//AIAgent relationshipsAgent;
//ExecutorBinding? relationshipsTripleAgentSubWorkflow = null;
//if (UseConcurrentRelationshipsAgents)
//{
//    Console.WriteLine("Using Concurrent Relationships Agents");
//    var relationshipsAgent1 = AgentHelper.BuildRelationshipsAgent(chatClient);
//    var relationshipsAgent2 = AgentHelper.BuildRelationshipsAgent(chatClient);
//    var relationshipsAgent3 = AgentHelper.BuildRelationshipsAgent(chatClient);
//    var relationshipsWorkflow = AgentWorkflowBuilder.BuildConcurrent(
//        [relationshipsAgent1, relationshipsAgent2, relationshipsAgent3],
//        WorkflowHelper.AggregateRelationships);
//    ////relationshipsAgent = relationshipsWorkflow.AsAgent(name: "RelationshipsTripleAgent", 
//    ////    description: "Aggregates the result from Relationship Agents");
//    relationshipsTripleAgentSubWorkflow = relationshipsWorkflow.BindAsExecutor("RelationshipsTripleAgentSubWorkflow");
//}
//else
//{
//    Console.WriteLine("Using Single Relationships Agent");
//    relationshipsAgent = AgentHelper.BuildRelationshipsAgent(chatClient);
//}

//// Run group chat or single agent for mermaid diagram generation
//AIAgent mermaidAgent;
//ExecutorBinding? mermaidAutocorrectiveSubWorkflow = null;
//if (useGroupChatAgents)
//{
//    Console.WriteLine("Using Group Chat Mermaid Agents");
//    var mermaidBuilderAgent = AgentHelper.BuildMermaidAgent(chatClient);
//    var validationAgent = AgentHelper.BuildValidationAgent(chatClient);
//    var mermaidWorkflow = AgentWorkflowBuilder
//        .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents) { MaximumIterationCount = 7 })
//        .AddParticipants(mermaidBuilderAgent, validationAgent)
//        .Build();
//    ////mermaidAgent = mermaidWorkflow.AsAgent(name: "MermaidAutocorrectiveAgent", 
//    ////    description: "Negotiates a valid mermaid diagram");
//    mermaidAutocorrectiveSubWorkflow = mermaidWorkflow.BindAsExecutor("MermaidAutocorrectiveSubWorkflow");
//}
//else
//{
//    Console.WriteLine("Using Single Mermaid Agent");
//    mermaidAgent = AgentHelper.BuildMermaidAgent(chatClient);
//}

//// Build main workflow
//////var workflow = AgentWorkflowBuilder.BuildSequential(entitiesAgent, relationshipsAgent, mermaidAgent);
//var workflow = new WorkflowBuilder(entitiesTripleAgentSubWorkflow!)
//    .AddEdge(entitiesTripleAgentSubWorkflow!, relationshipsTripleAgentSubWorkflow!)
//    .AddEdge(relationshipsTripleAgentSubWorkflow!, mermaidAutocorrectiveSubWorkflow!)
//    .WithOutputFrom(mermaidAutocorrectiveSubWorkflow!)
//    .Build();

//// Prepare input message with text and image
//var input = File.ReadAllText(Path.Combine("Data", "Input", "input.txt"));
//var query = $"""
//    ## CONTEXT
//    Input:
//    ```
//    {input}
//    ```
//    """;
//byte[] imageBytes = File.ReadAllBytes(Path.Combine("Data", "Input", "AMS Tech Conf.png"));
//ChatMessage msg = new(ChatRole.User, [
//    new TextContent(query),
//    new DataContent(imageBytes, "image/png")
//]);

//WorkflowHelper.PrintColoredLine($"""
//    QUERY:

//    {query}
//    """, ConsoleColor.Green);

//// Execute the workflow
//await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, input: msg);
//await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
//await WorkflowHelper.PrintWorkflowExecutionEventsAsync(run);
