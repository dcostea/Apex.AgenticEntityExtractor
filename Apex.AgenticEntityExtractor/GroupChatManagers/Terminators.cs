using Apex.AgenticEntityExtractor.Helpers;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.GroupChatManagers;

public class Terminators
{
    public static Func<RoundRobinGroupChatManager, IEnumerable<ChatMessage>, CancellationToken, ValueTask<bool>> TerminationFunction()
    {
        var terminationFunction = (RoundRobinGroupChatManager chatManager, IEnumerable<ChatMessage> messages, CancellationToken ct) =>
        {
            var lastText = messages.LastOrDefault()?.Text ?? "";
            bool isApproved = lastText.Contains("APPROVED", StringComparison.OrdinalIgnoreCase) &&
                !lastText.Contains("ERRORS FOUND", StringComparison.OrdinalIgnoreCase);

            // Access properties based on the actual type
            int currentIteration = chatManager is ApprovalRoundRobinGroupChatManager approvalManager 
                ? approvalManager.CurrentIterationCount 
                : chatManager.IterationCount;
            int maxIteration = chatManager.MaximumIterationCount;

            if (isApproved)
            {
                ConsoleHelper.PrintColoredLine($"\n[✓] Diagram APPROVED - Exiting review loop (iteration {currentIteration}/{maxIteration})\n", ConsoleColor.Green);
            }
            else if (currentIteration >= maxIteration)
            {
                ConsoleHelper.PrintColoredLine($"\n[!] Max iterations reached - Forcing approval\n", ConsoleColor.Yellow);
                return ValueTask.FromResult(true);
            }
            else
            {
                ConsoleHelper.PrintColoredLine($"\n[✗] Errors found - Retrying (iteration {currentIteration}/{maxIteration})\n", ConsoleColor.Red);
            }

            return ValueTask.FromResult(isApproved);
        };

        return terminationFunction;
    }
}
