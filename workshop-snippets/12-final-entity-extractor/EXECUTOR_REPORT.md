# Executor Inventory Report

> Auto-generated analysis of all custom `Executor`-derived classes in  
> `Apex.AgenticEntityExtractor.Executors` (5 executors).

---

## Summary Matrix

| Executor | Processing | Graph Role | Two-Phase | Output Mechanism | Stateful |
|---|---|---|---|---|---|
| **FanOutExecutor** | Conventional | Entry point | ✅ | `SendMessage` (broadcast) | Buffer |
| **BatcherExecutor** | Conventional | Relay | ❌ | `SendMessage` (forward) | Stateless |
| **AggregatorExecutor** | Conventional | Intermediate aggregator | ❌ | `SendMessage` (forward) | Accumulator |
| **ParticipantExecutor** | Agent-fueled | Agent wrapper | ✅ | `SendMessage` (forward) | Buffer |
| **RefinementExecutor** | Hybrid | Orchestrator hub | ✅ | `SendMessage` (targeted) + `YieldOutput` | Buffer + snapshot |

---

## Detailed Analysis

### 1. FanOutExecutor

- **Processing type:** Conventional code — pure buffering and broadcasting, no LLM involvement.
- **Graph role:** Fan-out entry point. First executor in a concurrent stage; broadcasts buffered messages to all downstream agents.
- **Message handlers (3):**
  - `HandleMessage(ChatMessage)` — sync, buffers single message (workflow entry input).
  - `HandleMessages(List<ChatMessage>)` — sync, buffers batch (inter-stage forwarded input from `AggregatorExecutor`).
  - `HandleTurnAsync(TurnToken)` — async, broadcasts buffer + `TurnToken` to all connected edges.
- **Two-phase pattern:** Yes — buffer first, broadcast on `TurnToken`.
- **Output mechanism:** `SendMessageAsync` (broadcast to all edges).
- **State:** `_messages` buffer; swap-and-clear on broadcast.
- **Notable:** Only executor with two buffer handlers (`ChatMessage` and `List<ChatMessage>`) because it can be either a workflow entry point or a mid-workflow receiver.

---

### 2. BatcherExecutor

- **Processing type:** Conventional code — stateless passthrough relay.
- **Graph role:** Per-branch identity node for fan-in barriers. Gives each branch a distinct executor ID so the barrier can track completion per agent.
- **Message handlers (1):**
  - `HandleMessagesAsync(List<ChatMessage>)` — async, immediately forwards messages unchanged.
- **Two-phase pattern:** No — single-phase passthrough (no `TurnToken` handling).
- **Output mechanism:** `SendMessageAsync` (forward).
- **State:** Stateless. `ResetAsync` is a no-op.
- **Notable:** Simplest executor. Exists purely for graph topology — without it the fan-in barrier cannot distinguish which agent produced which result. The async handler could technically be sync, but uses `async` since it awaits `SendMessageAsync`.

---

### 3. AggregatorExecutor

- **Processing type:** Conventional code — count-based barrier with injected aggregation function.
- **Graph role:** **Forwarding** (intermediate) fan-in aggregator. Merges results from N agents and sends downstream for further processing.
- **Message handlers (1):**
  - `HandleMessagesAsync(List<ChatMessage>)` — async, accumulates batches; merges and forwards when count reaches `numberOfConcurrentAgents`.
- **Two-phase pattern:** No — accumulate-and-forward (self-triggered on count threshold).
- **Output mechanism:** `SendMessageAsync` (forward `List<ChatMessage>` + `TurnToken`).
- **State:** `_agentResults` accumulator (`List<List<ChatMessage>>`).
- **Notable:** Forwards merged results downstream (enabling chained fan-out/fan-in stages in a single graph) rather than yielding terminal output.

---

### 4. ParticipantExecutor

- **Processing type:** **Agent-fueled** — invokes `AIAgent.RunStreamingAsync` to get LLM responses.
- **Graph role:** Agent wrapper for star-topology group chats. Bridges the `AIAgent` streaming interface with the executor-to-executor message protocol.
- **Message handlers (2):**
  - `HandleMessages(List<ChatMessage>)` — sync, buffers incoming messages.
  - `HandleTurnAsync(TurnToken)` — async, invokes agent in streaming mode, collects response, forwards result.
- **Two-phase pattern:** Yes — buffer messages, then invoke agent on `TurnToken`.
- **Output mechanism:** `SendMessageAsync` (forward `List<ChatMessage>` + `TurnToken`).
- **State:** `_messages` buffer.
- **Notable:** The only executor that directly calls an `AIAgent`. Supports live event emission (`AgentResponseUpdateEvent`) during streaming. The `includeInputInOutput` parameter controls whether full conversation context is preserved downstream.

---

### 5. RefinementExecutor

- **Processing type:** **Hybrid** — conventional orchestration logic that routes to agent-backed participants. Does not invoke `AIAgent` directly but controls which participant runs next.
- **Graph role:** Star-topology hub / group chat orchestrator. Central controller that manages turn-taking, termination, and output selection.
- **Message handlers (2):**
  - `HandleMessages(List<ChatMessage>)` — sync, buffers incoming messages.
  - `HandleTurnAsync(TurnToken)` — async, orchestrates a single group-chat turn.
- **Two-phase pattern:** Yes — buffer messages, orchestrate on `TurnToken`.
- **Output mechanism:** Both `SendMessageAsync` (**targeted** to specific executor ID) and `YieldOutputAsync` (terminal, on termination).
- **State:** `_messages` buffer + `_bestMermaidOutput` snapshot (captures the last valid Mermaid diagram as fallback).
- **Message routing:** Only executor using **targeted sends** (`context.SendMessageAsync(messages, targetExecutor.Id, ...)`), routing to a specific participant rather than broadcasting to all edges.
- **Notable:** Custom equivalent of the framework's internal `GroupChatHost`. Depends on `ApprovalManager` for termination checks, history filtering, and next-agent selection. Resets the manager's iteration counter in `ResetAsync`.

---

## Cross-Cutting Observations

### Two-Phase Message Protocol
Three of five executors follow the **buffer → TurnToken trigger** pattern. The exceptions are `BatcherExecutor` (stateless passthrough) and `AggregatorExecutor` (self-triggered on count threshold). The two-phase protocol decouples data arrival from processing activation, preventing premature execution on partial input.

### State & Lifecycle
All five executors declare `declareCrossRunShareable: true` and implement `IResettableExecutor`. Only `BatcherExecutor` has a true no-op reset; all others clear buffers or accumulators. `RefinementExecutor` additionally resets external state (`manager.CurrentIterationCount`).

### Agent Invocation
Only `ParticipantExecutor` directly calls `AIAgent.RunStreamingAsync`. `RefinementExecutor` orchestrates agents indirectly by routing to `ParticipantExecutor` instances. The remaining three executors are pure conventional code with no LLM interaction.

### Output Mechanism Split
- **`SendMessageAsync`** (forwarding): `FanOutExecutor`, `BatcherExecutor`, `AggregatorExecutor`, `ParticipantExecutor`
- **Both** `SendMessageAsync` + `YieldOutputAsync`: `RefinementExecutor` (targeted sends during orchestration, yield on termination)

### Sync vs Async Handlers
Buffer-phase handlers are consistently **sync** (`void`) across all executors — correct since they only assign a field. Action-phase handlers (`HandleTurnAsync`) are **async** (`ValueTask`) since they call `SendMessageAsync` or `YieldOutputAsync`.
