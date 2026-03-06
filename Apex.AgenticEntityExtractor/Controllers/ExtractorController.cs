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
  /// Runs the single-agent extraction path.
  /// </summary>
  [HttpPost("/extract/single-agent")]
  [Consumes("multipart/form-data")]
  public async Task<IActionResult> RunSingleAgentExtractionAsync([FromForm] ExtractionRequest request)
  {
    try
    {
      ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);

      // Build the single extractor agent.
      AIAgent extractorAgent = extractorAgentsBuilder.BuildExtractorAgent();

      // Run and render streamed output.
      await workflowHelper.RenderAgentResponseStreamAsync(extractorAgent, userMessage, "SINGLE AGENT EXTRACTION");

      return Ok();
    }
    catch (Exception ex)
    {
      return BadRequest(ex.Message);
    }
  }

  /// <summary>
  /// Runs the sequential workflow path.
  /// </summary>
  [HttpPost("/extract/workflow/sequential")]
  [Consumes("multipart/form-data")]
  public async Task<IActionResult> RunSequentialWorkflowAsync([FromForm] ExtractionRequest request)
  {
    try
    {
      ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);

      // Build workflow.
      Workflow workflow = extractorWorkflowBuilder.BuildSequentialPipeline("SequentialPipeline");
      await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, userMessage);

      // Trigger execution and render workflow events/output.
      await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
      var result = await workflowHelper.RenderWorkflowExecutionEventsAsync(run, "WORKFLOW WITH SIMPLE SEQUENTIAL AGENTS");

      return Ok(result);
    }
    catch (Exception ex)
    {
      return BadRequest(ex.Message);
    }
  }

  /// <summary>
  /// Runs the workflow-composition path where sub-workflows are wrapped as agents.
  /// </summary>
  [HttpPost("/extract/workflow/as-agents")]
  [Consumes("multipart/form-data")]
  public async Task<IActionResult> RunConcurrentWorkflowAsync([FromForm] ExtractionRequest request)
  {
    try
    {
      ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);

      // Build workflow.
      Workflow workflow = extractorWorkflowBuilder.BuildPipelineFromConcurrentWorkflows("PipelineFromConcurrentWorkflows");
      await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, userMessage);

      // Trigger execution and render workflow events/output.
      await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
      var result = await workflowHelper.RenderWorkflowExecutionEventsAsync(run, "WORKFLOW WITH WORKFLOWS AS AGENTS");

      return Ok(result);
    }
    catch (Exception ex)
    {
      return BadRequest(ex.Message);
    }
  }

  /// <summary>
  /// Runs the custom-orchestration sub-workflow composition path.
  /// </summary>
  [HttpPost("/extract/workflow/sub-workflows")]
  [Consumes("multipart/form-data")]
  public async Task<IActionResult> RunCustomOrchestrationsWorkflowAsync([FromForm] ExtractionRequest request)
  {
    try
    {
      ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);

      // Build workflow.
      Workflow workflow = extractorWorkflowBuilder.BuildPipelineFromCustomOrchestrations("PipelineFromCustomOrchestrations");
      await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, userMessage);

      // Trigger execution with event emission enabled.
      await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

      // Render workflow events/output.
      var result = await workflowHelper.RenderWorkflowExecutionEventsAsync(run, "WORKFLOW WITH SUBWORKFLOWS");

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
  [HttpPost("/extract/workflow/fully-custom")]
  [Consumes("multipart/form-data")]
  public async Task<IActionResult> RunFullyCustomWorkflowAsync([FromForm] ExtractionRequest request)
  {
    try
    {
      ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);
      workflowRenderer.PrintQueryAndInputImagePreviewAndWait(userMessage);

      // Build workflow.
      Workflow workflow = extractorWorkflowBuilder.BuildFullyCustomOrchestratedPipeline("FullyCustomPipeline");
      var mermaidFlow = workflow.ToMermaidString();

      await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, userMessage);

      // Trigger execution with event emission enabled.
      await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

      // Render workflow events/output.
      var result = await workflowHelper.RenderWorkflowExecutionEventsAsync(run, "FULLY CUSTOM PIPELINE");

      workflowRenderer.PrintMermaidFlowPreviewAndWait(mermaidFlow);

      return Ok(result);
    }
    catch (Exception ex)
    {
      return BadRequest(ex.Message);
    }
  }
}
