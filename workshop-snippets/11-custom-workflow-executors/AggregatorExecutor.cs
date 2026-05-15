using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

[SendsMessage(typeof(List<ChatMessage>))]
[YieldsOutput(typeof(ChatMessage))]
public partial class AggregatorExecutor(string executorId, int expectedResults)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  private readonly List<ChatMessage> _messages = [];

  [MessageHandler]
  private async ValueTask HandleMessagesAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken)
  {
    _messages.AddRange(messages.Where(message => message.Role == ChatRole.Assistant));

    if (_messages.Count < expectedResults)
    {
      return;
    }

    string mergedText = string.Join("\n\n", _messages.Select(message => message.Text));
    ChatMessage output = new(ChatRole.Assistant, mergedText);

    await context.SendMessageAsync(new List<ChatMessage> { output }, cancellationToken: cancellationToken);
    await context.YieldOutputAsync(output, cancellationToken);
  }

  public ValueTask ResetAsync()
  {
    _messages.Clear();
    return default;
  }
}
