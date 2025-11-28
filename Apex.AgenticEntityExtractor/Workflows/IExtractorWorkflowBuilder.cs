using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Apex.AgenticEntityExtractor.Workflows;

public interface IExtractorWorkflowBuilder
{
    Workflow BuildEntitiesSubWorkflow(string workflowName);
    Workflow BuildRelationshipsSubWorkflow(string workflowName);
    Workflow BuildMermaidSubWorkflow(string workflowName);
    Workflow BuildMainWorkflow();
    Workflow BuildMainWorkflowWithSubWorkflows();
    Workflow BuildMainWorkflowWithWorkflowsAsAgents();
}