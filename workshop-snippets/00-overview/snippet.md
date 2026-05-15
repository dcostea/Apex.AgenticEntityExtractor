# Agentic AI Workshop Snippets

Progressive snippets for the 6-hour workshop **Agentic AI on .NET with Microsoft Agent Framework and Small Language Models**.

Use these snippets as copy/paste milestones. Each folder introduces one concept and keeps the code intentionally small before moving toward the final `Apex.AgenticEntityExtractor` application.

## Progression

1. `01-minimal-agent-in-webapi` - Minimal Web API endpoint with one agent.
2. `02-add-devui` - Add Microsoft Agent Framework DevUI.
3. `03-add-tool` - Add deterministic ontology tools.
4. `04-switch-provider` - Switch between OpenAI, Azure OpenAI, and Ollama.
5. `05-add-middleware` - Add agent/tool middleware for caching and audit events.
6. `06-add-observability-aspire` - Add Aspire AppHost, ServiceDefaults, and OpenTelemetry.
7. `07-add-evaluation` - Add output evaluation for entity extraction quality.
8. `08-sequential-orchestration` - Build a sequential extraction pipeline.
9. `09-concurrent-orchestration` - Add fan-out/fan-in relationship extraction.
10. `10-handoff-orchestration` - Add specialization/escalation handoff.
11. `11-custom-workflow-executors` - Build custom executors and edges.
12. `12-final-entity-extractor` - Map the snippets to the existing full demo.

## Suggested delivery rhythm

- Start each module with the problem it solves.
- Paste only the delta for that module.
- Run the endpoint or DevUI after every stage.
- Use the final project as the reference architecture, not as the first code students see.

## Recommended workshop usage

- Use stages 01-04 for live coding.
- Use stages 05-07 for guided copy/paste and inspection.
- Use stages 08-11 for prepared demos with selective live modifications.
- Use stage 12 as the final architecture walkthrough.
