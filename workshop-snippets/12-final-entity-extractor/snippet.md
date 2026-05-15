# 12 - Final entity extractor

Goal: Connect all previous workshop concepts to the complete reference architecture.

## Project Structure

The `12-final-entity-extractor` project demonstrates production-style patterns:

### Organized Folders

```
├── Agents/
│   └── ExtractorAgentsBuilder.cs     # Centralized agent factory
├── Clients/
│   └── ExtractorChatClientBuilder.cs # Provider switching with full config
├── Enums/
│   └── ChatProvider.cs                # OpenAI | AzureOpenAI | Ollama
├── Middleware/
│   ├── IToolResponseMiddleware.cs
│   └── ToolResponseMiddleware.cs      # Caching with distributed cache
├── OutputRenderers/
│   ├── IWorkflowRenderer.cs
│   └── SpectreWorkflowRenderer.cs     # Console-based workflow visualization
├── Tools/
│   └── OntologyTools.cs               # Entity + relationship ontology loaders
├── Workflows/
│   ├── IExtractorWorkflowBuilder.cs
│   └── ExtractorWorkflowBuilder.cs    # High-level + low-level workflow patterns
├── Data/
│   ├── Instructions/                  # Agent instruction markdown files
│   └── Ontology/                      # Entity/relationship type definitions
└── Program.cs                         # Minimal composition root
```

### Key Patterns Demonstrated

| Concept | Implementation |
|---------|---------------|
| Minimal agent | `ExtractorAgentsBuilder.BuildSoloAgent()` |
| DevUI | `builder.AddDevUI()`, `builder.AddAIAgent()`, `builder.AddWorkflow()` |
| Tools | `OntologyTools` with ontology JSON files |
| Provider switching | `ExtractorChatClientBuilder` + `appsettings.json` |
| Middleware | `ToolResponseMiddleware` with distributed cache |
| Observability | `WorkflowBuilder.WithOpenTelemetry()` |
| Sequential orchestration | `BuildHighLevelPatterns` outer pipeline |
| Concurrent orchestration | `BuildConcurrentRelationshipExtraction` |
| Handoff orchestration | *(not included in final, covered in step 10)* |
| Custom executors | `BuildLowLevelFullCustomWorkflow` with `FanOutExecutor`, `AggregatorExecutor` |

### Configuration (appsettings.json)

```json
{
  "Provider": "OpenAI",
  "ToolResponseCacheTTL": "01:00:00",
  "OpenAI": {
    "ModelId": "gpt-4.1-mini"
  },
  "AzureOpenAI": {
    "Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
    "DeploymentName": "YOUR-DEPLOYMENT"
  },
  "Ollama": {
    "Server": "http://localhost:11434",
    "Model": "gemma4:e4b",
    "Model2": "gemma4:e4b"
  }
}
```

### Endpoints

| Endpoint | Description |
|----------|-------------|
| `POST /extract/agents/solo` | Single agent with no tools (baseline) |
| `POST /extract/entities` | Tool-enabled entity extraction |
| `POST /extract/patterns` | High-level orchestration patterns (sequential + concurrent) |
| `POST /extract/workflows` | Low-level custom workflow with fan-out/fan-in |
| `/devui` | Interactive agent and workflow playground |

### Running the Final Project

```bash
cd workshop-snippets/12-final-entity-extractor
dotnet run
```

Navigate to:
- Swagger UI: `https://localhost:7078/swagger`
- DevUI: `https://localhost:7078/devui`

### DevUI Registered Agents and Workflows

- Agents: `ExtractorSoloAgent`, `EntAgent_1`, `RelAgent_1`, `RelAgent_2`, `ReporterAgent`, `AnalystAgent`
- Workflows: `PipelineFromConcurrentWorkflows`, `FullCustomWorkflow`

### Teaching Walkthrough

1. Show `appsettings.json` provider selection
2. Run solo agent (`POST /extract/agents/solo`) — baseline with no tools
3. Run entity agent (`POST /extract/entities`) — tool-enabled with ontology
4. Open DevUI and explore agents
5. Run high-level orchestration (`POST /extract/patterns`)
6. View workflow diagram in DevUI
7. Run custom workflow (`POST /extract/workflows`)
8. Explain custom executors, fan-out/fan-in, aggregation
9. Show middleware cache hits/misses in console
10. *(Optional)* Add Aspire Dashboard for OpenTelemetry traces

## Teaching Points

- This is a **reference architecture** for production agentic AI in .NET
- Clear separation of concerns: agents, clients, middleware, workflows, rendering
- Interface-driven design enables testing and extensibility
- Configuration-driven provider switching
- DevUI makes complex workflows interactive and debuggable
- Middleware keeps cross-cutting concerns separate
- Custom executors provide full control when high-level patterns don't fit
