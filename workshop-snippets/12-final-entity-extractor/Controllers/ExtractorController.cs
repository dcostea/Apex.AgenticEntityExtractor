using Apex.AgenticEntityExtractor.Agents;
using Apex.AgenticEntityExtractor.Enums;
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
public class ExtractorController(IExtractorWorkflowBuilder extractorWorkflowBuilder, IExtractorAgentsBuilder extractorAgentsBuilder, WorkflowHelper workflowHelper, IWorkflowRenderer workflowRenderer, IConfiguration configuration) : ControllerBase
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
      Microsoft.Extensions.AI.ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);

      // Build the single extractor agent.
      AIAgent extractorAgent = extractorAgentsBuilder.BuildSoloAgent(configuration.GetValue<ChatProvider>("Provider"));

      // Run and render streamed output.
      string? result = await workflowHelper.RenderAgentResponseStreamAsync(extractorAgent, userMessage, "USING SOLO AGENT");

      return Ok(result);
    }
    catch (Exception ex)
    {
      return BadRequest(ex.Message);
    }
  }

  /// <summary>
  /// Runs the entity extraction agent only, using the entities ontology tool.
  /// </summary>
  [HttpPost("/extract/entities")]
  [Consumes("multipart/form-data")]
  public async Task<IActionResult> RunEntitiesAgentAsync([FromForm] ExtractionRequest request)
  {
    try
    {
      ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);

      AIAgent entitiesAgent = extractorAgentsBuilder.BuildEntitiesAgent(provider: configuration.GetValue<ChatProvider>("Provider"));

      Entities result = await workflowHelper.RenderAgentResponseAsync(entitiesAgent, userMessage, "USING ENTITIES AGENT");

      return Ok(result);
    }
    catch (Exception ex)
    {
      return BadRequest(ex.Message);
    }
  }

  /// <summary>
  /// Runs the workflow-from-concurrent-workflows path.
  /// </summary>
  [HttpPost("/extract/patterns")]
  [Consumes("multipart/form-data")]
  public async Task<IActionResult> RunHighLevelPatternsAsync([FromForm] ExtractionRequest request)
  {
    try
    {
      ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);

      // Build workflow.
      Workflow workflow = extractorWorkflowBuilder.BuildHighLevelPatterns("OrchestrationPatterns");
      await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, userMessage);

      // Trigger execution with event emission enabled.
      await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

      // Render workflow events/output.
      var result = await workflowHelper.RenderWorkflowExecutionEventsAsync(run, "USING ORCHESTRATION PATTERNS");

      var mermaidFlow = workflow.ToMermaidString();
      workflowRenderer.PrintMermaidFlowPreview(mermaidFlow);

      var dotFlow = WorkflowVisualizer.ToDotString(workflow);
      workflowRenderer.PrintDotFlowPreview(dotFlow);

      workflowRenderer.PrintQuery(userMessage);
      workflowRenderer.PrintInputImage(userMessage);

      return Ok(result);
    }
    catch (Exception ex)
    {
      return BadRequest(ex.Message);
    }
  }

  /// <summary>
  /// Runs the fully custom low-level orchestration workflow.
  /// </summary>
  [HttpPost("/extract/workflows")]
  [Consumes("multipart/form-data")]
  public async Task<IActionResult> RunLowLevelWorkflowAsync([FromForm] ExtractionRequest request)
  {
    try
    {
      ChatMessage userMessage = await MessageHelper.BuildUserMessageAsync(request);

      // Build workflow.
      Workflow workflow = extractorWorkflowBuilder.BuildLowLevelFullCustomWorkflow("FullCustomWorkflow");

      await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, userMessage);

      // Trigger execution with event emission enabled.
      await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

      // Render workflow events/output.
      var result = await workflowHelper.RenderWorkflowExecutionEventsAsync(run, "USING FULL CUSTOM WORKFLOW");

      var mermaidFlow = workflow.ToMermaidString();
      workflowRenderer.PrintMermaidFlowPreview(mermaidFlow);

      var dotFlow = WorkflowVisualizer.ToDotString(workflow);
      workflowRenderer.PrintDotFlowPreview(dotFlow);

      workflowRenderer.PrintQuery(userMessage);
      workflowRenderer.PrintInputImage(userMessage);

      return Ok(result);
    }
    catch (Exception ex)
    {
      return BadRequest(ex.Message);
    }
  }
}
