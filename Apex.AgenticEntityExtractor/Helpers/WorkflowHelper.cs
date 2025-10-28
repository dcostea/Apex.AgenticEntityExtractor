using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AgenticEntityExtractor.Helpers;

public static class WorkflowHelper
{
    public static async Task PrintWorkflowFinalMessageAsync(StreamingRun run)
    {
        await foreach (var evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent outputEvent)
            {
                PrintColoredLine($"{outputEvent.SourceId}: {outputEvent.As<List<ChatMessage>>()?.LastOrDefault()?.Text}", ConsoleColor.Yellow);
            }
        }
    }

    public static async Task PrintAgentResponseStreamAsync(string query, AIAgent workflowAgent)
    {
        string? lastAuthor = null;
        await foreach (var update in workflowAgent.RunStreamingAsync(query))
        {
            // when new author, print author header
            if (lastAuthor != update.AuthorName)
            {
                lastAuthor = update.AuthorName;
                PrintColoredLine($"\n\n** {update.AuthorName} **", ConsoleColor.Gray);
            }

            PrintColored(update.Text, ConsoleColor.Green);
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
                        Console.WriteLine($"TOOL CALL [{toolCall.CallId}] {toolCall.Name} {arguments}");
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
                        Console.WriteLine($"TOOL RESP [{toolResult.CallId}] {toolResult.Result} {annotations}");
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

    public static async Task PrintWorkflowExecutionEventsAsync(StreamingRun run)
    {
        string? lastExecutorId = null;
        var workflowStopwatch = Stopwatch.StartNew();
        var executorTimings = new Dictionary<string, Stopwatch>();
        var executorDurations = new Dictionary<string, TimeSpan>();

        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case SuperStepStartedEvent stepStarted:
                    PrintStepHeader(stepStarted.StepNumber);
                    PrintEventWithData(evt);
                    if (stepStarted.Data is SuperStepStartInfo startInfo)
                    {
                        PrintColoredLine($"Sending Executors: {string.Join(", ", startInfo.SendingExecutors.Select(s => s.Split('_')[0]))}", ConsoleColor.Yellow);
                    }
                    break;

                case SuperStepCompletedEvent stepCompleted:
                    PrintEventWithData(evt);
                    if (stepCompleted.Data is SuperStepCompletionInfo completionInfo)
                    {
                        PrintColoredLine($"Activated Executors: {string.Join(", ", completionInfo.ActivatedExecutors.Select(s => s.Split('_')[0]))}", ConsoleColor.Yellow);
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
                    PrintColoredLine($"[{evt.GetType().Name}]", ConsoleColor.Red);
                    Console.WriteLine((error.Data as TargetInvocationException)?.Message);
                    workflowStopwatch.Stop();
                    PrintExecutionSummary(executorDurations, workflowStopwatch.Elapsed);
                    break;

                default:
                    PrintColoredLine($"[{evt.GetType().Name}]", ConsoleColor.Red);
                    Console.WriteLine(JsonSerializer.Serialize(evt));
                    break;
            }
        }

        workflowStopwatch.Stop();
        PrintExecutionSummary(executorDurations, workflowStopwatch.Elapsed);
    }


    public static void PrintColoredLine(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    public static void PrintColored(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ResetColor();
    }

    // Private helper methods
    private static void PrintStepHeader(int stepNumber)
    {
        Console.WriteLine($"***** Step: {stepNumber} *************************************************************************************************");
    }

    private static void PrintEventWithData(WorkflowEvent evt)
    {
        PrintColored($"[{evt.GetType().Name}] ", ConsoleColor.DarkGray);
    }

    private static void PrintEventWithExecutor(WorkflowEvent evt, string executorId)
    {
        PrintColoredLine($"\n[{evt.GetType().Name}] {executorId.Split('_')[0]}", ConsoleColor.DarkGray);
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
        PrintColoredLine($"[{evt.GetType().Name}] {JsonSerializer.Serialize(data)}", ConsoleColor.DarkGray);
    }

    private static bool PrintRunUpdateEvent(AgentRunUpdateEvent e, ref string? lastExecutorId)
    {
        if (string.IsNullOrEmpty(e.Update.Text))
        {
            PrintToolCalls(e.Update.Contents.OfType<FunctionCallContent>());
            return false;
        }

        if (e.ExecutorId != lastExecutorId)
        {
            lastExecutorId = e.ExecutorId;
            Console.WriteLine();
            PrintColoredLine($"[AgentRunUpdateEvent] {e.ExecutorId.Split('_')[0]}:", ConsoleColor.DarkGray);
            Console.WriteLine();
        }

        PrintColored(e.Update.Text, ConsoleColor.Green);

        return true;
    }

    private static void PrintToolCalls(IEnumerable<FunctionCallContent> calls)
    {
        if (!calls.Any()) return;

        Console.WriteLine();
        foreach (var call in calls)
        {
            PrintColoredLine($"[TOOL CALL: {call.Name}] with arguments: {JsonSerializer.Serialize(call.Arguments)}", ConsoleColor.Blue);

            if (call.Arguments?.TryGetValue("reasonForHandoff", out var reason) == true) 
            {
                PrintColoredLine($"  Handoff Reason: {reason}", ConsoleColor.Yellow);
            }
        }
    }

    private static void PrintWorkflowOutput(WorkflowOutputEvent output)
    {
        PrintColoredLine($"[{output.GetType().Name}] {output.SourceId}", ConsoleColor.DarkGray);
        Console.WriteLine("\nRESPONSE:\n");

        var final = output.As<List<ChatMessage>>()?.LastOrDefault()?.Text;
        if (!string.IsNullOrWhiteSpace(final))
        {
            PrintColoredLine(final, ConsoleColor.Yellow);
        }

        Console.WriteLine("***** Run Complete *************************************************************************************************");
        Console.WriteLine();
    }

    private static void PrintExecutionSummary(Dictionary<string, TimeSpan> executorDurations, TimeSpan totalTime)
    {
        Console.WriteLine();
        PrintColoredLine("***** Execution Summary *************************************************************************************************", ConsoleColor.Magenta);
        Console.WriteLine();
        
        if (executorDurations.Count != 0)
        {
            Console.WriteLine("Executor/Agent Execution Times:");
            foreach (var kvp in executorDurations.OrderByDescending(x => x.Value))
            {
                var executorName = kvp.Key.Split('_')[0];
                Console.WriteLine($"  {executorName}: {kvp.Value.TotalSeconds:F2}s ({kvp.Value.TotalMilliseconds:F0}ms)");
            }
            Console.WriteLine();
        }

        Console.WriteLine($"TOTAL Workflow Execution Time: {totalTime.TotalSeconds:F2}s ({totalTime.TotalMilliseconds:F0}ms)");
        Console.WriteLine("***** End Summary *************************************************************************************************");
        Console.WriteLine();
    }
}
