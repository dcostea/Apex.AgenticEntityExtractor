using Apex.AgenticEntityExtractor.Models;
using Apex.AgenticEntityExtractor.OutputRenderers;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// <b>Entity Normalization Gate</b> — sits between the entity agent and the relationship
/// fan-out. Deduplicates entities and assigns deterministic sequential IDs (e1, e2, …)
/// before the relationship agents see them.
///
/// <b>Why this is necessary:</b>
/// The entity agent's LLM output may:
/// <list type="bullet">
///   <item>Omit <c>id</c> fields entirely (structured-output LLMs vary).</item>
///   <item>Emit duplicate entities (same type+value with different IDs).</item>
/// </list>
/// Relationship agents reference entity IDs in their output. If those IDs are based on a
/// raw (undeduped) entity list, they diverge from the IDs <see cref="Aggregator.AggregateRelationships"/>
/// assigns after deduplication — making every relationship reference invalid.
///
/// <b>How it works:</b>
/// Passes the original User messages (source text + image) forward unchanged, and replaces
/// the entity agent's Assistant message with a normalized <c>{"entities":[...]}</c> JSON
/// using clean sequential IDs and no duplicates.
/// </summary>
[SendsMessage(typeof(List<ChatMessage>))]
[SendsMessage(typeof(TurnToken))]
internal sealed partial class NormalizeEntitiesExecutor(string executorId)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

  private List<ChatMessage> _messages = [];

  [MessageHandler]
  private void HandleMessages(List<ChatMessage> messages, IWorkflowContext context)
  {
    _messages = messages;
  }

  [MessageHandler]
  private async ValueTask HandleTurnAsync(TurnToken token, IWorkflowContext context, CancellationToken cancellationToken)
  {
    if (_messages.Count == 0)
      return;

    List<ChatMessage> messages = _messages;
    _messages = [];

    // Parse, deduplicate, and re-assign deterministic IDs
    Entities? raw = PayloadHelper.TryParseLatestStructuredPayload<Entities>(messages);
    if (raw?.Items is { Count: > 0 } rawItems)
    {
      List<IGrouping<string, Entity>> groups = [.. rawItems.GroupBy(e => $"{e.EntityType}|{e.EntityValue}", StringComparer.OrdinalIgnoreCase)];

      List<Entity> normalized = [];
      for (int i = 0; i < groups.Count; i++)
      {
        Entity first = groups[i].First();
        normalized.Add(new Entity { Id = $"e{i + 1}", EntityType = first.EntityType, EntityValue = first.EntityValue });
      }

      // Keep User messages (original source) and replace entity JSON with the normalized version
      List<ChatMessage> userMessages = messages.Where(m => m.Role == ChatRole.User).ToList();
      string normalizedJson = JsonSerializer.Serialize(new Entities { Items = normalized }, IndentedOptions);
      userMessages.Add(new ChatMessage(ChatRole.Assistant, normalizedJson));
      messages = userMessages;
    }

    await context.SendMessageAsync(messages, cancellationToken: cancellationToken);
    await context.SendMessageAsync(new TurnToken(emitEvents: token.EmitEvents is true), cancellationToken: cancellationToken);
  }

  public ValueTask ResetAsync()
  {
    _messages = [];
    return default;
  }
}
