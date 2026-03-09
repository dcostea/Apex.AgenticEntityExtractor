using Apex.AgenticEntityExtractor.Agents;
using Apex.AgenticEntityExtractor.Helpers;
using Apex.AgenticEntityExtractor.Models;
using Apex.AgenticEntityExtractor.OutputRenderers;
using Apex.AgenticEntityExtractor.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Controllers;

/// <summary>
/// API endpoints for running extraction agents/workflows with text and optional image input.
/// </summary>
[ApiController]
[Route("[controller]")]
public class ExtractorController(IExtractorWorkflowBuilder extractorWorkflowBuilder, IExtractorAgentsBuilder extractorAgentsBuilder, WorkflowHelper workflowHelper, IWorkflowRenderer workflowRenderer) : ControllerBase
{
  /// <summary>
  /// Runs the solo-agent extraction path.
  /// </summary>
  [HttpPost("/extract/agents/solo")]
  [Consumes("multipart/form-data")]
  public async Task<IActionResult> RunSoloAgentAsync([FromForm] ExtractionRequest request)
  {
    try
    {
      ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);

      // Build the single extractor agent.
      AIAgent extractorAgent = extractorAgentsBuilder.BuildSoloAgent();

      // Run and render streamed output.
      string? result = await workflowHelper.RenderAgentResponseStreamAsync(extractorAgent, userMessage, "SOLO AGENT EXTRACTION");

      return Ok(result);
    }
    catch (Exception ex)
    {
      return BadRequest(ex.Message);
    }
  }

  /// <summary>
  /// Runs the pipeline-from-concurrent-workflows path.
  /// </summary>
  [HttpPost("/extract/patterns")]
  [Consumes("multipart/form-data")]
  public async Task<IActionResult> RunHighLevelPatternsAsync([FromForm] ExtractionRequest request)
  {
    try
    {
      ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);

      // Build workflow.
      Workflow workflow = extractorWorkflowBuilder.BuildHighLevelPatterns("PipelineFromConcurrentWorkflows");
      await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, userMessage);

      // Trigger execution and render workflow events/output.
      await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
      var result = await workflowHelper.RenderWorkflowExecutionEventsAsync(run, "PIPELINE FROM CONCURRENT WORKFLOWS");

      return Ok(result);
    }
    catch (Exception ex)
    {
      return BadRequest(ex.Message);
    }
  }

  /// <summary>
  /// Runs the fully custom low-level orchestration pipeline.
  /// </summary>
  [HttpPost("/extract/workflows")]
  [Consumes("multipart/form-data")]
  public async Task<IActionResult> RunLowLevelWorkflowAsync([FromForm] ExtractionRequest request)
  {
    try
    {
      ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);
      workflowRenderer.PrintQueryAndInputImagePreviewAndWait(userMessage);

      // Build workflow.
      Workflow workflow = extractorWorkflowBuilder.BuildLowLevelFullCustomWorkflow("FullCustomWorkflow");
      var mermaidFlow = workflow.ToMermaidString();

      await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, userMessage);

      // Trigger execution with event emission enabled.
      await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

      // Render workflow events/output.
      var result = await workflowHelper.RenderWorkflowExecutionEventsAsync(run, "FULL CUSTOM WORKFLOW");

      workflowRenderer.PrintMermaidFlowPreviewAndWait(mermaidFlow);

      return Ok(result);
    }
    catch (Exception ex)
    {
      return BadRequest(ex.Message);
    }
  }
}
