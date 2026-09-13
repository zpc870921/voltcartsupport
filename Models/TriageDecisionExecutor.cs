using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace customersupport.Models
{
    public class TriageDecisionExecutor(): Executor<List<ChatMessage>, TriageDecision>("parser-triage")
    {
        public override async ValueTask<TriageDecision> HandleAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            var json = messages.LastOrDefault(x=>x.Role==ChatRole.Assistant)?.Text;
            var decision = JsonSerializer.Deserialize<TriageDecision>(json) ?? throw new InvalidOperationException($"Triage response was not valid json:{json}");
            return decision;
        }
    }
}
