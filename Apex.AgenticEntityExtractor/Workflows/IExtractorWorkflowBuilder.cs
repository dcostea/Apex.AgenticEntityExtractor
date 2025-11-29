using Microsoft.Agents.AI.Workflows;

namespace Apex.AgenticEntityExtractor.Workflows;

public interface IExtractorWorkflowBuilder
{
    Workflow BuildWorkflowFromSequentialWorkflow(string workflowName);
    Workflow BuildWorkflowFromSubWorkflows(string workflowName);
    Workflow BuildWorkflowFromWorkflowsAsAgents(string workflowName);

    Workflow BuildEntitiesSubWorkflow(string workflowName);
    Workflow BuildRelationshipsSubWorkflow(string workflowName);
    Workflow BuildMermaidSubWorkflow(string workflowName);
}
