using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

[SendsMessage(typeof(List<ChatMessage>))]
[SendsMessage(typeof(TurnToken))]
public partial class FanOutExecutor(string executorId)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  private List<ChatMessage> _messages = [];

  [MessageHandler]
  private void HandleMessage(ChatMessage message, IWorkflowContext context)
  {
    _messages.Add(message);
  }

  [MessageHandler]
  private void HandleMessages(List<ChatMessage> messages, IWorkflowContext context)
  {
    _messages.AddRange(messages);
  }

  [MessageHandler]
  private async ValueTask HandleTurnAsync(TurnToken token, IWorkflowContext context, CancellationToken cancellationToken)
  {
    if (_messages.Count == 0)
    {
      return;
    }

    List<ChatMessage> messages = _messages;
    _messages = [];

    await context.SendMessageAsync(messages, cancellationToken: cancellationToken);
    await context.SendMessageAsync(new TurnToken(emitEvents: token.EmitEvents is true), cancellationToken: cancellationToken);
  }

  public ValueTask ResetAsync()
  {
    _messages = [];
    return default;
  }
}
