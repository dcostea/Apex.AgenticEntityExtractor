using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.RegularExpressions;
using Apex.AgenticEntityExtractor.Models;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// <b>Mermaid Validator Executor</b> — a deterministic (non-LLM) executor that
/// validates a Mermaid diagram against the structured entity and relationship
/// data carried in the conversation history.
///
/// <b>Role in the graph:</b> Can replace or supplement the LLM reviewer participant
/// with rule-based validation, eliminating hallucination risk for structural checks.
///
/// <b>Two-phase message handling:</b>
/// <list type="number">
///   <item><see cref="HandleMessages"/> — buffers incoming <c>List&lt;ChatMessage&gt;</c>.</item>
///   <item><see cref="HandleTurnAsync"/> — triggered by a <see cref="TurnToken"/>; performs
///         validation on the buffered messages, then forwards the result and a new
///         <see cref="TurnToken"/> downstream. If no messages were buffered (spurious token),
///         the handler returns silently.</item>
/// </list>
///
/// <b>Validation pipeline (inside <see cref="HandleTurnAsync"/>):</b>
/// <list type="number">
///   <item>Extracts the latest <c>```mermaid</c> code block from the conversation.</item>
///   <item>Deserialises the latest <c>```json</c> block into entity and relationship lists.</item>
///   <item>Parses Mermaid nodes (<c>id[label]</c>) and edges (<c>src --&gt;|rel| tgt</c>).</item>
///   <item>Cross-references parsed diagram elements with the structured data to detect
///         missing entities, invented entities, incorrect labels, missing relationships,
///         and invented relationships.</item>
///   <item>Emits <c>"APPROVED"</c> when no errors are found, or <c>"ERRORS FOUND\n…"</c>
///         with a detailed error list otherwise.</item>
/// </list>
/// </summary>
[SendsMessage(typeof(List<ChatMessage>))]
[SendsMessage(typeof(TurnToken))]
public partial class MermaidValidatorExecutor(string executorId)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  private List<ChatMessage> _messages = [];

  /// <summary>Phase 1: buffer incoming messages until a <see cref="TurnToken"/> triggers processing.</summary>
  [MessageHandler]
  private void HandleMessages(List<ChatMessage> messages, IWorkflowContext context)
  {
    _messages = messages;
  }

  /// <summary>Phase 2: validate the buffered messages when a <see cref="TurnToken"/> arrives.</summary>
  [MessageHandler]
  private async ValueTask HandleTurnAsync(TurnToken token, IWorkflowContext context, CancellationToken cancellationToken)
  {
    // No messages buffered — spurious TurnToken, nothing to validate
    if (_messages.Count == 0)
      return;

    // Swap-and-clear to avoid reprocessing on subsequent TurnTokens
    List<ChatMessage> messages = _messages;
    _messages = [];

    // Step 1: Locate the most recent ```mermaid code block in the conversation
    string mermaidCode = ExtractMermaidCode(messages);
    if (string.IsNullOrEmpty(mermaidCode))
    {
      await SendErrorAsync(context, "No Mermaid code block found in messages.", token, cancellationToken);
      return;
    }

    // Step 2: Deserialise structured entity/relationship data from the latest ```json block
    var entities = ExtractEntities(messages);
    var relationships = ExtractRelationships(messages);

    // Step 3: Parse Mermaid syntax into node/edge collections
    var (nodes, edges) = ParseMermaid(mermaidCode);

    // Step 4: Cross-reference diagram elements against structured data
    var errors = ValidateDiagram(nodes, edges, entities, relationships);

    // Step 5: Emit approval or detailed error list
    string result = errors.Count == 0 ? "APPROVED" : $"ERRORS FOUND\n{string.Join("\n", errors)}";

    var resultMessages = new List<ChatMessage> { new(ChatRole.Assistant, result) };
    await context.SendMessageAsync(resultMessages, cancellationToken: cancellationToken).ConfigureAwait(false);

    await context.SendMessageAsync(new TurnToken(emitEvents: token.EmitEvents is true), cancellationToken: cancellationToken).ConfigureAwait(false);
  }

  private static string ExtractMermaidCode(List<ChatMessage> messages)
  {
    foreach (var msg in messages.AsEnumerable().Reverse())
    {
      if (msg.Text is not null)
      {
        var match = Regex.Match(msg.Text, @"```mermaid\s*(.*?)\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match.Success)
        {
          return match.Groups[1].Value.Trim();
        }
      }
    }
    return string.Empty;
  }

  private static List<Entity> ExtractEntities(List<ChatMessage> messages)
  {
    foreach (var msg in messages.AsEnumerable().Reverse())
    {
      if (msg.Text is not null)
      {
        var match = Regex.Match(msg.Text, @"```json\s*(\{.*?\})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match.Success)
        {
          try
          {
            var json = JsonSerializer.Deserialize<JsonElement>(match.Groups[1].Value);
            if (json.TryGetProperty("entities", out var entitiesArray))
            {
              return JsonSerializer.Deserialize<List<Entity>>(entitiesArray.GetRawText()) ?? [];
            }
          }
          catch { }
        }
      }
    }
    return [];
  }

  private static List<Relationship> ExtractRelationships(List<ChatMessage> messages)
  {
    foreach (var msg in messages.AsEnumerable().Reverse())
    {
      if (msg.Text is not null)
      {
        var match = Regex.Match(msg.Text, @"```json\s*(\{.*?\})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match.Success)
        {
          try
          {
            var json = JsonSerializer.Deserialize<JsonElement>(match.Groups[1].Value);
            if (json.TryGetProperty("relationships", out var relationshipsArray))
            {
              return JsonSerializer.Deserialize<List<Relationship>>(relationshipsArray.GetRawText()) ?? [];
            }
          }
          catch { }
        }
      }
    }
    return [];
  }

  private static (Dictionary<string, string> nodes, List<(string source, string relationship, string target)> edges) ParseMermaid(string code)
  {
    var nodes = new Dictionary<string, string>();
    var edges = new List<(string, string, string)>();

    var lines = code.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    foreach (var line in lines)
    {
      // Node: id[label]
      var nodeMatch = Regex.Match(line, @"^(\w+)\[([^\]]+)\]$");
      if (nodeMatch.Success)
      {
        nodes[nodeMatch.Groups[1].Value] = nodeMatch.Groups[2].Value;
        continue;
      }

      // Edge: source -->|relationship| target
      var edgeMatch = Regex.Match(line, @"^(\w+)\s*-->\|([^|]+)\|\s*(\w+)$");
      if (edgeMatch.Success)
      {
        edges.Add((edgeMatch.Groups[1].Value, edgeMatch.Groups[2].Value.Trim(), edgeMatch.Groups[3].Value));
      }
    }

    return (nodes, edges);
  }

  private static List<string> ValidateDiagram(Dictionary<string, string> nodes, List<(string source, string relationship, string target)> edges, List<Entity> entities, List<Relationship> relationships)
  {
    var errors = new List<string>();

    // Check missing entities
    var entityIds = entities.Select(e => e.Id).ToHashSet();
    var nodeIds = nodes.Keys.ToHashSet();
    var missingEntities = entityIds.Except(nodeIds);
    if (missingEntities.Any())
    {
      errors.Add($"Missing Entities: {string.Join(", ", missingEntities)}");
    }

    // Check invented entities
    var inventedEntities = nodeIds.Except(entityIds);
    if (inventedEntities.Any())
    {
      errors.Add($"Invented Entities: {string.Join(", ", inventedEntities)}");
    }

    // Check node labels match entity type:value
    foreach (var entity in entities)
    {
      if (nodes.TryGetValue(entity.Id, out var label))
      {
        var expected = $"{entity.EntityType}:{entity.EntityValue}";
        if (label != expected)
        {
          errors.Add($"Incorrect Node Label: {entity.Id} expected '{expected}' but got '{label}'");
        }
      }
    }

    // Check missing relationships
    var relationshipSet = relationships.Select(r => (r.Source, r.RelationshipType, r.Target)).ToHashSet();
    var edgeSet = edges.Select(e => (e.source, e.relationship, e.target)).ToHashSet();
    var missingRelationships = relationshipSet.Except(edgeSet);
    if (missingRelationships.Any())
    {
      errors.Add($"Missing Relationships: {string.Join(", ", missingRelationships.Select(r => $"{r.Item1} -->|{r.Item2}| {r.Item3}"))}");
    }

    // Check invented relationships
    var inventedRelationships = edgeSet.Except(relationshipSet);
    if (inventedRelationships.Any())
    {
      errors.Add($"Invented Relationships: {string.Join(", ", inventedRelationships.Select(r => $"{r.Item1} -->|{r.Item2}| {r.Item3}"))}");
    }

    return errors;
  }

  private static async ValueTask SendErrorAsync(IWorkflowContext context, string error, TurnToken token, CancellationToken cancellationToken)
  {
    var resultMessages = new List<ChatMessage> { new(ChatRole.Assistant, $"ERRORS FOUND\n{error}") };
    await context.SendMessageAsync(resultMessages, cancellationToken: cancellationToken).ConfigureAwait(false);
    await context.SendMessageAsync(new TurnToken(emitEvents: token.EmitEvents is true), cancellationToken: cancellationToken).ConfigureAwait(false);
  }

  public ValueTask ResetAsync()
  {
    _messages = [];
    return default;
  }
}