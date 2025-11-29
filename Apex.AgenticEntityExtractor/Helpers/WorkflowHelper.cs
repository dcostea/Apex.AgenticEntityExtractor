using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.Helpers;

public static class WorkflowHelper
{
    public static async Task PrintAgentResponseStreamAsync(AIAgent agent, ChatMessage message, string header)
    {
        PrintExecutionHeader(header);

        ConsoleHelper.PrintColoredLine($"""
            QUERY:

            {message.Text}
            """, ConsoleColor.Green);

        string? lastAuthor = null;
        await foreach (var update in agent.RunStreamingAsync(message))
        {
            // when new author, print author header
            if (lastAuthor != update.AuthorName)
            {
                lastAuthor = update.AuthorName;
                ConsoleHelper.PrintColoredLine($"** {update.AuthorName} **", ConsoleColor.Yellow);
            }

            ConsoleHelper.PrintColored(update.Text, ConsoleColor.Yellow);
        }
    }

    public static async Task PrintWorkflowExecutionEventsAsync(StreamingRun run, ChatMessage message, string header)
    {
        string? lastExecutorId = null;
        var workflowStopwatch = Stopwatch.StartNew();
        var executorTimings = new Dictionary<string, Stopwatch>();
        var executorDurations = new Dictionary<string, TimeSpan>();

        PrintExecutionHeader(header);

        ConsoleHelper.PrintColoredLine($"""
            QUERY:

            {message.Text}
            """, ConsoleColor.Green);

        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case SuperStepStartedEvent stepStarted:
                    PrintStepHeader(stepStarted.StepNumber);
                    PrintEventWithData(evt);
                    if (stepStarted.Data is SuperStepStartInfo startInfo)
                    {
                        ConsoleHelper.PrintColoredLine($"Sending Executors: {string.Join(", ", startInfo.SendingExecutors.Select(s => s.Split('_')[0]))}", ConsoleColor.Yellow);
                    }
                    break;

                case SuperStepCompletedEvent stepCompleted:
                    PrintEventWithData(evt);
                    if (stepCompleted.Data is SuperStepCompletionInfo completionInfo)
                    {
                        ConsoleHelper.PrintColoredLine($"Activated Executors: {string.Join(", ", completionInfo.ActivatedExecutors.Select(s => s.Split('_')[0]))}", ConsoleColor.Yellow);
                    }
                    Console.WriteLine();
                    break;

                case ExecutorInvokedEvent invoked:
                    PrintEventWithExecutor(evt, invoked.ExecutorId);
                    // Start timing for this executor
                    if (!executorTimings.TryGetValue(invoked.ExecutorId, out Stopwatch? invokedStopwatch))
                    {
                        executorTimings[invoked.ExecutorId] = Stopwatch.StartNew();
                    }
                    else
                    {
                        invokedStopwatch.Restart();
                    }
                    break;

                case ExecutorCompletedEvent completed:
                    PrintEventWithExecutor(evt, completed.ExecutorId);
                    // Stop timing for this executor
                    if (executorTimings.TryGetValue(completed.ExecutorId, out var completedStopwatch))
                    {
                        completedStopwatch.Stop();
                        if (!executorDurations.ContainsKey(completed.ExecutorId))
                        {
                            executorDurations[completed.ExecutorId] = completedStopwatch.Elapsed;
                        }
                        else
                        {
                            executorDurations[completed.ExecutorId] += completedStopwatch.Elapsed;
                        }
                    }
                    break;

                case ExecutorFailedEvent failed:
                    PrintEventWithExecutor(evt, failed.ExecutorId);
                    Console.WriteLine(failed.Data?.Message);
                    // Stop timing for failed executor
                    if (executorTimings.TryGetValue(failed.ExecutorId, out var failedStopwatch))
                    {
                        failedStopwatch.Stop();
                        if (!executorDurations.ContainsKey(failed.ExecutorId))
                        {
                            executorDurations[failed.ExecutorId] = failedStopwatch.Elapsed;
                        }
                        else
                        {
                            executorDurations[failed.ExecutorId] += failedStopwatch.Elapsed;
                        }
                    }
                    break;

                case WorkflowStartedEvent or WorkflowWarningEvent or RequestInfoEvent:
                    PrintEventWithSerializedData(evt);
                    break;

                case AgentRunUpdateEvent update:
                    if (!PrintRunUpdateEvent(update, ref lastExecutorId)) continue;
                    break;

                case AgentRunResponseEvent:
                    PrintEventWithSerializedData(evt);
                    break;

                case WorkflowOutputEvent output:
                    workflowStopwatch.Stop();
                    PrintWorkflowOutput(output);
                    PrintExecutionSummary(executorDurations, workflowStopwatch.Elapsed);
                    return;

                case WorkflowErrorEvent error:
                    ConsoleHelper.PrintColoredLine($"[{evt.GetType().Name}]", ConsoleColor.Red);
                    Console.WriteLine((error.Data as TargetInvocationException)?.Message);
                    workflowStopwatch.Stop();
                    PrintExecutionSummary(executorDurations, workflowStopwatch.Elapsed);
                    break;

                default:
                    ConsoleHelper.PrintColoredLine($"[{evt.GetType().Name}]", ConsoleColor.Red);
                    Console.WriteLine(JsonSerializer.Serialize(evt));
                    break;
            }
        }

        workflowStopwatch.Stop();
        PrintExecutionSummary(executorDurations, workflowStopwatch.Elapsed);
    }

    public static async Task PrintWorkflowFinalMessageAsync(StreamingRun run, ChatMessage message, string header)
    {
        PrintExecutionHeader(header);

        ConsoleHelper.PrintColoredLine($"""
            QUERY:

            {message.Text}
            """, ConsoleColor.Green);

        await foreach (var evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent outputEvent)
            {
                ConsoleHelper.PrintColoredLine($"{outputEvent.SourceId}: {outputEvent.As<List<ChatMessage>>()?.LastOrDefault()?.Text}", ConsoleColor.Yellow);
            }
        }
    }


    public static void PrintTools(List<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.Assistant)
            {
                foreach (var content in message.Contents)
                {
                    if (content is FunctionCallContent toolCall)
                    {
                        var arguments = toolCall.Arguments is null ? "" : JsonSerializer.Serialize(toolCall.Arguments);
                        ConsoleHelper.PrintColoredLine($"TOOL CALL [{toolCall.CallId}] {toolCall.Name} {arguments}", ConsoleColor.Blue);
                    }
                }
            }
            if (message.Role == ChatRole.Tool)
            {
                foreach (var content in message.Contents)
                {
                    if (content is FunctionResultContent toolResult)
                    {
                        var annotations = toolResult.Annotations is null ? "" : JsonSerializer.Serialize(toolResult.Annotations);
                        ConsoleHelper.PrintColoredLine($"TOOL RESP [{toolResult.CallId}] {toolResult.Result} {annotations}", ConsoleColor.Blue);
                    }
                }
            }
        }
        Console.ResetColor();
    }

    public static async Task PrintToMarkdownAsync(Workflow workflow)
    {
        var mermaid = workflow.ToMermaidString();
        var markdown = $"# Workflow Diagram\n\n```mermaid\n{mermaid}\n```\n";
        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var filePath = Path.Combine(projectDir, "workflow.md");
        await File.WriteAllTextAsync(filePath, markdown);
    }


    // Private helper methods

    private static void PrintExecutionHeader(string header)
    {
        Console.WriteLine();
        ConsoleHelper.PrintColoredLine($"***** {header} ************************************************************************************", ConsoleColor.Yellow);
        Console.WriteLine();
    }

    // Private helper methods
    private static void PrintStepHeader(int stepNumber)
    {
        ConsoleHelper.PrintColoredLine($"***** STEP: {stepNumber} *************************************************************************************************", ConsoleColor.Yellow);
    }

    private static void PrintEventWithData(WorkflowEvent evt)
    {
        ConsoleHelper.PrintColored($"[{evt.GetType().Name}] ", ConsoleColor.DarkGray);
    }

    private static void PrintEventWithExecutor(WorkflowEvent evt, string executorId)
    {
        ConsoleHelper.PrintColoredLine($"\n[{evt.GetType().Name}] {executorId.Split('_')[0]}", ConsoleColor.DarkGray);
    }

    private static void PrintEventWithSerializedData(WorkflowEvent evt)
    {
        object? data = evt switch
        {
            WorkflowStartedEvent wse => wse.Data,
            WorkflowWarningEvent wwe => wwe.Data,
            RequestInfoEvent rie => rie.Data,
            _ => null
        };
        ConsoleHelper.PrintColoredLine($"[{evt.GetType().Name}] {JsonSerializer.Serialize(data)}", ConsoleColor.DarkGray);
    }

    private static bool PrintRunUpdateEvent(AgentRunUpdateEvent e, ref string? lastExecutorId)
    {
        if (string.IsNullOrEmpty(e.Update.Text))
        {
            // Tool calls are logged by the FunctionCallMiddleware with cache status
            return false;
        }

        // Use AuthorName if available (for group chat agents), otherwise use ExecutorId
        string agentIdentifier = !string.IsNullOrEmpty(e.Update.AuthorName)
            ? e.Update.AuthorName
            : e.ExecutorId.Split('_')[0];

        if (agentIdentifier != lastExecutorId)
        {
            lastExecutorId = agentIdentifier;
            Console.WriteLine();
            ConsoleHelper.PrintColoredLine($"[{agentIdentifier}]:", ConsoleColor.Yellow);
            Console.WriteLine();
        }

        ConsoleHelper.PrintColored(e.Update.Text, ConsoleColor.Green);

        return true;
    }

    private static void PrintWorkflowOutput(WorkflowOutputEvent output)
    {
        ConsoleHelper.PrintColoredLine($"[{output.GetType().Name}] {output.SourceId}", ConsoleColor.DarkGray);
        ConsoleHelper.PrintColoredLine("\nRESPONSE:\n", ConsoleColor.Yellow);

        var messages = output.As<List<ChatMessage>>();
        var final = messages?.LastOrDefault()?.Text;
        if (!string.IsNullOrWhiteSpace(final))
        {
            ConsoleHelper.PrintColoredLine(final, ConsoleColor.Yellow);
        }
        else
        {
            ConsoleHelper.PrintColoredLine("WARNING: No final message text found!", ConsoleColor.Red);
        }

        ConsoleHelper.PrintColoredLine("***** Run Complete *************************************************************************************************", ConsoleColor.Yellow);
        Console.WriteLine();
    }

    private static void PrintExecutionSummary(Dictionary<string, TimeSpan> executorDurations, TimeSpan totalTime)
    {
        ConsoleHelper.PrintColoredLine("***** Execution Summary *************************************************************************************************", ConsoleColor.Yellow);
        Console.WriteLine();

        if (executorDurations.Count != 0)
        {
            ConsoleHelper.PrintColoredLine("Executor/Agent Execution Times:", ConsoleColor.Yellow);
            foreach (var kvp in executorDurations)
            {
                var executorName = kvp.Key.Split('_')[0];
                ConsoleHelper.PrintColoredLine($"  {executorName}: {kvp.Value.TotalSeconds:F2}s", ConsoleColor.Yellow);
            }
            Console.WriteLine();
        }

        ConsoleHelper.PrintColoredLine($"TOTAL Workflow Execution Time: {totalTime.TotalSeconds:F2}s", ConsoleColor.Yellow);
        ConsoleHelper.PrintColoredLine("***** End Summary *************************************************************************************************", ConsoleColor.Yellow);
        Console.WriteLine();
    }
}
