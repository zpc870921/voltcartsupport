using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace customersupport.Models
{
   // [YieldsOutput(typeof(ChatMessage))]
    public class SpeciallistExecutor : Executor<TriageDecision, ChatMessage>
    {
        private readonly AIAgent _agent;
        public SpeciallistExecutor(string id, AIAgent agent):base(id)
        {
            _agent = agent;
        }
        public override async ValueTask<ChatMessage> HandleAsync(TriageDecision message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            var response = await _agent.RunAsync(message.Summary,cancellationToken:cancellationToken);
            var msg = response.Messages.LastOrDefault(x => x.Role == ChatRole.Assistant);
           // await context.YieldOutputAsync(msg,cancellationToken);
            return msg;
        }
    }
}
