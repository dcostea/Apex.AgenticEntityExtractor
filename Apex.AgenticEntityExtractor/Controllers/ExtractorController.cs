using Apex.AgenticEntityExtractor.Helpers;
using Apex.AgenticEntityExtractor.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Controllers;

[ApiController]
[Route("[controller]")]
public class ExtractorController(IServiceProvider sp) : ControllerBase
{
    [HttpPost("/extract")]
    public async Task<IActionResult> RunExtractionWorkflowAsync()
    {
        try
        {
            var workflow = sp.GetRequiredKeyedService<Workflow>("ExtractionWorkflow");

            // Prepare input message with text and image
            var input = System.IO.File.ReadAllText(Path.Combine("Data", "Input", "input.txt"));
            var query = $"""
                ## CONTEXT
                Input:
                ```
                {input}
                ```
                """;
            byte[] imageBytes = System.IO.File.ReadAllBytes(Path.Combine("Data", "Input", "AMS Tech Conf.png"));
            ChatMessage userMessage = new(ChatRole.User, 
            [
                new TextContent(query),
                new DataContent(imageBytes, "image/png")
            ]);

            WorkflowHelper.PrintColoredLine($"""
                QUERY:

                {query}
                """, ConsoleColor.Green);

            // Execute the workflow
            await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, input: userMessage);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
            await WorkflowHelper.PrintWorkflowExecutionEventsAsync(run);

            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}