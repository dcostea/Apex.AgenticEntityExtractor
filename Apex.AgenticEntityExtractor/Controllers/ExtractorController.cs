using Apex.AgenticEntityExtractor.Agents;
using Apex.AgenticEntityExtractor.Helpers;
using Apex.AgenticEntityExtractor.Models;
using Apex.AgenticEntityExtractor.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Controllers;

[ApiController]
[Route("[controller]")]
public class ExtractorController(IExtractorWorkflowBuilder extractorWorkflowBuilder, IExtractorAgentsBuilder extractorAgentsBuilder) : ControllerBase
{
    [HttpPost("/extract/single-agent")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RunSingleAgentAsync([FromForm] ExtractionRequest request)
    {
        try
        {
            // Prepare input message with text and image
            var input = request.InputText ?? System.IO.File.ReadAllText(Path.Combine("Data", "Input", "input.txt"));
            var query = $"""
                ## CONTEXT
                Input:
                ```
                {input}
                ```
                """;

            byte[] imageBytes;
            string contentType;
            if (request.InputImage != null && request.InputImage.Length > 0)
            {
                using var memoryStream = new MemoryStream();
                await request.InputImage.CopyToAsync(memoryStream);
                imageBytes = memoryStream.ToArray();
                contentType = request.InputImage.ContentType;
            }
            else
            {
                imageBytes = System.IO.File.ReadAllBytes(Path.Combine("Data", "Input", "input.png"));
                contentType = "image/png";
            }

            ChatMessage userMessage = new(ChatRole.User,
            [
                new TextContent(query),
                new DataContent(imageBytes, contentType)
            ]);

            // Build the "god" agent
            AIAgent extractorAgent = extractorAgentsBuilder.BuildExtractorAgent();

            // Execute the agent and print the output
            await WorkflowHelper.PrintAgentResponseStreamAsync(extractorAgent, userMessage, "WORKFLOW WITH SIMPLE SEQUENTIAL AGENTS");

            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("/extract/workflow/sequential")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RunSequentialWorkflowAsync([FromForm] ExtractionRequest request)
    {
        try
        {
            // Prepare input message with text and image
            var input = request.InputText ?? System.IO.File.ReadAllText(Path.Combine("Data", "Input", "input.txt"));
            var query = $"""
                ## CONTEXT
                Input:
                ```
                {input}
                ```
                """;

            byte[] imageBytes;
            string contentType;
            if (request.InputImage != null && request.InputImage.Length > 0)
            {
                using var memoryStream = new MemoryStream();
                await request.InputImage.CopyToAsync(memoryStream);
                imageBytes = memoryStream.ToArray();
                contentType = request.InputImage.ContentType;
            }
            else
            {
                imageBytes = System.IO.File.ReadAllBytes(Path.Combine("Data", "Input", "input.png"));
                contentType = "image/png";
            }

            ChatMessage userMessage = new(ChatRole.User,
            [
                new TextContent(query),
                new DataContent(imageBytes, contentType)
            ]);

            // Build the main workflow
            Workflow mainWorkflow = extractorWorkflowBuilder.BuildWorkflowFromSequentialWorkflow("WorkflowFromSequentialWorkflow");
            await using StreamingRun run = await InProcessExecution.StreamAsync(mainWorkflow, userMessage);

            // Execute the workflow, emit events and print the output
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
            await WorkflowHelper.PrintWorkflowExecutionEventsAsync(run, userMessage, "WORKFLOW WITH SIMPLE SEQUENTIAL AGENTS");

            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }


    [HttpPost("/extract/workflow/as-agents")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RunWorkflowWithWorkflowsAsAgentsAsync([FromForm] ExtractionRequest request)
    {
        try
        {
            // Prepare input message with text and image
            var input = request.InputText ?? System.IO.File.ReadAllText(Path.Combine("Data", "Input", "input.txt"));
            var query = $"""
                ## CONTEXT
                Input:
                ```
                {input}
                ```
                """;

            byte[] imageBytes;
            string contentType;
            if (request.InputImage != null && request.InputImage.Length > 0)
            {
                using var memoryStream = new MemoryStream();
                await request.InputImage.CopyToAsync(memoryStream);
                imageBytes = memoryStream.ToArray();
                contentType = request.InputImage.ContentType;
            }
            else
            {
                imageBytes = System.IO.File.ReadAllBytes(Path.Combine("Data", "Input", "input.png"));
                contentType = "image/png";
            }

            ChatMessage userMessage = new(ChatRole.User,
            [
                new TextContent(query),
                new DataContent(imageBytes, contentType)
            ]);

            // Build the main workflow from subworkflows
            Workflow mainWorkflow = extractorWorkflowBuilder.BuildWorkflowFromWorkflowsAsAgents("WorkflowFromWorkflowsAsAgents");
            await using StreamingRun run = await InProcessExecution.StreamAsync(mainWorkflow, userMessage);

            // Execute the workflow, emit events and print the output
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
            await WorkflowHelper.PrintWorkflowExecutionEventsAsync(run, userMessage, "WORKFLOW WITH WORKFLOWS AS AGENTS");

            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("/extract/workflow/sub-workflows")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RunWorkflowWithSubWorkflowsAsync([FromForm] ExtractionRequest request)
    {
        try
        {
            // Prepare input message with text and image
            var input = request.InputText ?? System.IO.File.ReadAllText(Path.Combine("Data", "Input", "input.txt"));
            var query = $"""
                ## CONTEXT
                Input:
                ```
                {input}
                ```
                """;

            byte[] imageBytes;
            string contentType;
            if (request.InputImage != null && request.InputImage.Length > 0)
            {
                using var memoryStream = new MemoryStream();
                await request.InputImage.CopyToAsync(memoryStream);
                imageBytes = memoryStream.ToArray();
                contentType = request.InputImage.ContentType;
            }
            else
            {
                imageBytes = System.IO.File.ReadAllBytes(Path.Combine("Data", "Input", "input.png"));
                contentType = "image/png";
            }

            ChatMessage userMessage = new(ChatRole.User,
            [
                new TextContent(query),
                new DataContent(imageBytes, contentType)
            ]);

            // Build the main workflow from subworkflows
            Workflow mainWorkflow = extractorWorkflowBuilder.BuildWorkflowFromSubWorkflows("WorkflowFromSubWorkflows");
            await using StreamingRun run = await InProcessExecution.StreamAsync(mainWorkflow, userMessage);

            // Execute the workflow, emit events and print the output
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
            await WorkflowHelper.PrintWorkflowExecutionEventsAsync(run, userMessage, "WORKFLOW WITH SUBWORKFLOWS");

            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
