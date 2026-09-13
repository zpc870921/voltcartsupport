
using customersupport.Models;
using customersupport.Security;
using customersupport.Tools;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.ClientModel;
using System.Diagnostics;

namespace customersupport
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var activitySource = new ActivitySource("voltcartsupport");
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService("voltcartsupport"))
                .WithTracing(tracing =>
                {
                    tracing.AddSource("voltcartsupport")
                    .AddSource("Experimental.Microsoft.Agents.AI")
                    .AddSource("Experimental.Microsoft.Agents.AI.Workflow")
                    .AddSource("Experimental.Microsoft.Agents.AI.*")
                    .AddOtlpExporter(config =>
                    {
                        config.Endpoint = new Uri("http://localhost:4317");
                    })
                    .AddConsoleExporter();
                });

            var openAIConfig = builder.Configuration.GetSection(OpenAiModel.SectionName).Get<OpenAiModel>();
            var openAiClient = new OpenAIClient(new ApiKeyCredential(openAIConfig.ApiKey), new OpenAIClientOptions
            {
                Endpoint = new Uri(openAIConfig.EndPoint)
            });
            var chatClient = openAiClient
                .GetChatClient(openAIConfig.ModelId)
                .AsIChatClient()
                .AsBuilder()
                .UseOpenTelemetry(sourceName: "voltcartsupport", configure: config =>
                {
                    config.EnableSensitiveData = true;
                })
                .Build();
            builder.Services.AddChatClient(chatClient);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();
            builder.Services.Configure<OpenAiModel>(builder.Configuration.GetSection(OpenAiModel.SectionName));


            var triageAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Instructions = """
                  You are the triage agent for VoltCart customer support.
                  Analyze the CUSTOMER'S message and return a TriageDecision.
                  TargetAgent must be exactly one of: "orders", "billing", "technical",
                  Confidence is between 0.0 and 1.0.
                  Summary is a one-sentence description of the customer's issue.
                  CustomerSentiment is one of: "frustrated", "neutral", "positive"
                  Respond with ONLY the JSON object - no prose, no code fences.
                  """,
                     ResponseFormat=ChatResponseFormat.ForJsonSchema<TriageDecision>(),
                    Tools = [AIFunctionFactory.Create(CustomerTools.GetCustomerInfo)]
                },
                Name = "triageAgent",

            })
                .AsBuilder()
                .Use(runFunc:InputSanitation.GuardraiMiddleware,runStreamingFunc:null)
                .UseOpenTelemetry("voltcartsupport", configure: config =>
                {
                    config.EnableSensitiveData = true;
                })
                .Build();

            var ordersAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Instructions = """
                  You are the orders specialist for VoltCart.
                  Help customers with order lookups, shipping status, and cancellations.
                  Be concise and helpful. Always confirm the order ID with the customer.
                  If a customer wants to cancel, check the order status first.
                  """,
                    Tools = [
                        AIFunctionFactory.Create(OrderTools.LookupOrder),
                        AIFunctionFactory.Create(OrderTools.GetOrderStatus),
                        AIFunctionFactory.Create(OrderTools.CancelOrder)
                    ]
                },
                Name = "ordersAgent",

            })
                .AsBuilder()
                .UseOpenTelemetry("voltcartsupport", configure: config =>
                {
                    config.EnableSensitiveData = true;
                })
                .Build();
            var billingAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Instructions = """
                  You are the billing specialist for VoltCart
                    Help customers with invoices and payment history.
                    For refund requests, explain that Voltdart follows a confirmed refund workflow before submission.
                    Be empathetic
                  """,
                    Tools = [
                        AIFunctionFactory.Create(BillingTools.GetInvoice),
                        AIFunctionFactory.Create(BillingTools.ProcessRefund),
                        AIFunctionFactory.Create(BillingTools.GetPaymentHistory)
                    ]
                },
                Name = "billingAgent",
            })
            .AsBuilder()
            .UseOpenTelemetry("voltcartsupport", configure: config =>
            {
                config.EnableSensitiveData = true;
            })
            .Build();

            var technicalAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Instructions = """
                  You are the technical support specialist for VoltCart.
                  Search the knowledge base first before creating a support ticket.
                  Walk customers through solutions step by step.
                  """,
                    Tools = [
                       AIFunctionFactory.Create(TechnicalTools.CreateSupportTicket),
                        AIFunctionFactory.Create(TechnicalTools.SearchKnowledgeBase)
                   ]
                },
                Name = "technicalAgent",
            })
           .AsBuilder()
           .UseOpenTelemetry("voltcartsupport", configure: config =>
           {
               config.EnableSensitiveData = true;
           })
           .Build();

            builder.Services.AddOpenAIResponses();
            builder.Services.AddOpenAIConversations();
            builder.AddDevUI();

            builder.Services.AddAIAgent("triageAgent", (sp, key) => triageAgent);
            builder.Services.AddAIAgent("ordersAgent", (sp, key) => ordersAgent);
            builder.Services.AddAIAgent("billingAgent", (sp, key) => billingAgent);
            builder.Services.AddAIAgent("technicalAgent", (sp, key) => technicalAgent);

            builder.AddWorkflow("TriagleSupport", (sp, key) =>
            {
                var triage = sp.GetRequiredKeyedService<AIAgent>("triageAgent");
                var orders = sp.GetRequiredKeyedService<AIAgent>("ordersAgent");
                var billing = sp.GetRequiredKeyedService<AIAgent>("billingAgent");
                var technical = sp.GetRequiredKeyedService<AIAgent>("technicalAgent");

                var triageBinding = triage.BindAsExecutor(new AIAgentHostOptions
                {
                    ForwardIncomingMessages = false
                });
                var parser = new TriageDecisionExecutor();

                var ordersRun = new SpeciallistExecutor("orders-run", orders);
                var billingRun = new SpeciallistExecutor("billing-run", billing);
                var technicalRun = new SpeciallistExecutor("technical-run", technical);

                var workflow = new WorkflowBuilder(triageBinding)
                .WithName("TriagleSupport")
                .AddEdge(triageBinding, parser)
                .AddEdge(parser, ordersRun, GetCondition("orders"))
                .AddEdge(parser, billingRun, GetCondition("billing"))
                .AddEdge(parser, technicalRun, GetCondition("technical"))
                .WithOutputFrom(ordersRun, billingRun, technicalRun)
                .WithOpenTelemetry(activitySource: activitySource, configure: config => config.EnableSensitiveData = true)
                .Build();
                return workflow;
            });

            static Func<object?, bool> GetCondition(string agentName) => result => result is TriageDecision triageDecision && triageDecision.TargetAgent == agentName;

            var app = builder.Build();
            app.MapOpenAIResponses();
            app.MapOpenAIConversations();
            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapDevUI();
            }

            app.UseAuthorization();


            app.MapControllers();


            app.Run();
        }
    }
}
