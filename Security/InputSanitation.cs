using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace customersupport.Security
{
    public static class InputSanitation
    {
        public static async Task<AgentResponse> GuardraiMiddleware(IEnumerable<ChatMessage> messages,AgentSession? session,AgentRunOptions? options,AIAgent innerAgent,CancellationToken cancellationToken)
        {
            var lastMessages = messages.LastOrDefault().Text.ToLower();
            string[] blockedWords = ["password","secret","credentials"];
            foreach (var blockedWord in blockedWords)
            {
                if(lastMessages.Contains(blockedWord))
                {
                    Console.WriteLine($"[GUARDRAIL] blocked request containing '{blockedWord}'");
                    return new AgentResponse(new ChatMessage(ChatRole.Assistant, $"sorry,i cannot process requests related to '{blockedWord}'"));
                }
            }

            var response = await innerAgent.RunAsync(session,options,cancellationToken);
            var lastResponseMessage = response.Messages.LastOrDefault().Text;
            if(lastResponseMessage.Length>5000)
            {
                Console.WriteLine($"[GUARDRAIL] response too long,truncating...");
                return new AgentResponse(new ChatMessage(ChatRole.Assistant, lastResponseMessage[..5000]));
            }
            return response;
        }
    }
}
