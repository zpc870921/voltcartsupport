# 🚀 一行代码接入GPT？.NET 10 + Microsoft Agent Framework 让我惊了

> **从0到1搭建AI客服系统，手把手带你跑通第一个Agent工作流**

---

## 📖 引子：.NET 也能"玩"AI Agent？

去年这个时候，如果你跟我说"用 C# 写 AI Agent"，我一定会笑出来——这领域不是 Python 的天下吗？

但 2026 年，微软终于把"AI Agent 开发"这件事做到了 .NET 里，而且还做得相当优雅。

今天我就用一个**真实可跑的项目**——**VoltCart 智能客服系统**，带你从 0 到 1 把每一个 Agent、每一条 Workflow 都拆透。

> 这个项目用 .NET 10 + `Microsoft.Agents.AI` 搭建了一个多智能体协作的客服系统：先由 AI 自动判断用户问题属于"订单/账单/技术"哪一类，再路由给对应的智能体处理，最后把结果汇总给用户。

全程不到 300 行核心代码，却实现了：

- ✅ 4 个 AI 智能体协同（triageAgent / ordersAgent / billingAgent / technicalAgent）
- ✅ AI 自动调用 9 个工具函数（查订单、退款、查发票、查知识库…）
- ✅ 强类型 JSON 输出（拒绝"AI 偶尔少个逗号"）
- ✅ 输入安全护栏（敏感词过滤 + 超长截断）
- ✅ 全链路 OpenTelemetry 追踪
- ✅ 一键启动可视化调试界面

不信？跟我一起看 👇

---

## 🧱 一、先搞清楚我们要解决什么问题

想象你是 VoltCart 电商的客服主管，每天面对这些问题：

- "我的订单 ORD-1002 怎么还没到？"（订单类）
- "上个月发票开错了，能重开吗？"（账单类）
- "智能音箱连不上 WiFi 怎么办？"（技术类）

传统做法：雇一堆客服，人工分类、人工回答。

**AI 时代的做法**：

```
┌──────────┐    ┌──────────┐    ┌──────────┐
│  用户消息 │───▶│ triageAgent │───▶│ 路由决策 │
└──────────┘    └──────────┘    └────┬─────┘
                                      │
                    ┌─────────────────┼─────────────────┐
                    ▼                 ▼                 ▼
             ┌──────────┐      ┌──────────┐      ┌──────────┐
             │ordersAgent│      │billingAgent│    │technicalAgent│
             │ 订单智能体│      │ 账单智能体│      │ 技术智能体│
             └─────┬────┘      └─────┬────┘      └─────┬────┘
                   ▼                 ▼                 ▼
              ┌────────┐        ┌────────┐        ┌────────┐
              │OrderTools│     │BillingTools│    │TechTools│
              └────────┘        └────────┘        └────────┘
```

而这一切，在 .NET 里只需要下面这些文件 👇

---

## 🛠️ 二、项目结构（5分钟看懂）

```
📦 VoltCartSupport
├── Program.cs                       ← 入口，组装所有 Agent + Workflow
├── Models/
│   ├── OpenAiModel.cs               ← 模型配置（兼容 OpenAI 协议）
│   ├── TriageDecision.cs            ← 分诊结果强类型
│   ├── TriageDecisionExecutor.cs    ← 把分诊 Agent 的 JSON 输出解析为 TriageDecision
│   └── SpeciallistExecutor.cs       ← 智能体的工作流执行器
├── Tools/                           ← 9 个 AI 可调用的工具函数
│   ├── CustomerTools.cs             ← 查客户信息
│   ├── OrderTools.cs                ← 查订单 / 查状态 / 取消订单
│   ├── BillingTools.cs              ← 查发票 / 处理退款 / 查支付历史
│   └── TechnicalTools.cs            ← 搜知识库 / 建工单
├── Security/
│   └── InputSanitation.cs           ← 输入安全护栏中间件
└── Controllers/                     ← 标准 ASP.NET Core MVC
```

---

## 🧠 三、底层：聊天客户端与配置

### 3.1 配置类——兼容任何 OpenAI 协议的模型

```csharp
public class OpenAiModel
{
    public const string SectionName = "OpenAI";
    public string ApiKey { get; set; }
    public string EndPoint { get; set; }
    public string ModelId { get; set; }
}
```

`appsettings.json`：

```json
{
  "OpenAI": {
    "ApiKey": "sk-xxxxxxxxxxxxxxxx",
    "EndPoint": "https://api.openai.com/v1",
    "ModelId": "gpt-4o-mini"
  }
}
```

### 3.2 创建聊天客户端——一行接 OpenTelemetry

```csharp
var openAIConfig = builder.Configuration
    .GetSection(OpenAiModel.SectionName).Get<OpenAiModel>();

var openAiClient = new OpenAIClient(
    new ApiKeyCredential(openAIConfig.ApiKey),
    new OpenAIClientOptions { Endpoint = new Uri(openAIConfig.EndPoint) });

var chatClient = openAiClient
    .GetChatClient(openAIConfig.ModelId)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(sourceName: "voltcartsupport", configure: c => c.EnableSensitiveData = true)
    .Build();

builder.Services.AddChatClient(chatClient);
```

**划重点**：

- 改 `EndPoint` 就能换 DeepSeek / 通义千问 / Ollama，**不改一行业务代码**
- `.UseOpenTelemetry()` 一行打通 LLM 全链路追踪（Token、延迟、异常、Prompt 内容）
- `AddChatClient` 把它注册成 DI 服务，后续 `Worker` 里直接注入即可

---

## 🤖 四、第一个智能体：triageAgent（分诊台）

**职责**：用户进来后第一站，读懂用户的问题，判断该转给谁。

### 4.1 分诊结果的数据契约

```csharp
public record TriageDecision(
    [Description("目标Agent: orders / billing / technical")]
    [property: JsonPropertyName("targetAgent")] string TargetAgent,

    [Description("置信度 0.0 ~ 1.0")]
    [property: JsonPropertyName("confidence")]    double Confidence,

    [property: JsonPropertyName("summary")]
    string Summary,

    [Description("客户情绪: frustrated / neutral / positive")]
    [property: JsonPropertyName("customerSentiment")] string CustomerSentiment);
```

`record` + `Description` + `JsonPropertyName` = **强类型 + 自文档化 + 自动反序列化** 三合一。

### 4.2 分诊智能体的灵魂配置

```csharp
var triageAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new ChatOptions
    {
        Instructions = """
            你是 VoltCart 的分诊 Agent。
            分析用户消息,返回 TriageDecision:
            - targetAgent: "orders" / "billing" / "technical"
            - confidence: 0.0 ~ 1.0
            - summary: 一句话概括用户问题
            - customerSentiment: "frustrated" / "neutral" / "positive"
            只返回 JSON,不要解释。
        """,
        ResponseFormat = ChatResponseFormat.ForJsonSchema<TriageDecision>(),
        Tools = [AIFunctionFactory.Create(CustomerTools.GetCustomerInfo)]
    },
    Name = "triageAgent"
})
.AsBuilder()
.Use(runFunc: InputSanitation.GuardraiMiddleware)   // 👈 挂载安全护栏
.UseOpenTelemetry("voltcartsupport")
.Build();
```

**三个亮点**：

1️⃣ `ResponseFormat.ForJsonSchema<T>()` —— 强制大模型按 C# 类输出结构化 JSON，告别"JSON 偶尔少逗号"

2️⃣ `AIFunctionFactory.Create(...)` —— 把任意静态方法注册成 AI 工具，无需写 schema

3️⃣ `.Use(...)` —— Agent 中间件机制，支持在请求前/响应后插任何逻辑

### 4.3 它能调用的工具：CustomerTools

```csharp
public static class CustomerTools
{
    [Description("用客户ID或邮箱查询客户信息")]
    public static string GetCustomerInfo(
        [Description("客户ID或邮箱")] string identifier)
    {
        return identifier.ToLowerInvariant() switch
        {
            "cust-001" or "alice@example.com"
                => "Customer:Alice Johnson(CUST-001)...3 orders on file",
            "cust-002" or "bob@example.com"
                => "Customer:Bob Smith(CUST-002)...1 orders on file",
            _ => $"未找到客户 '{identifier}'"
        };
    }
}
```

> 💡 **小窍门**：`[Description]` 既是给 AI 看的"工具说明书"，也是给开发者看的"API 文档"。这就是为什么 Agent 项目建议用 static 方法 + 显式描述——比 Function Calling 的 JSON Schema 写法清爽 10 倍。

---

## 📦 五、第二个智能体：ordersAgent（订单智能体）

**职责**：查订单、查物流、取消订单。

### 5.1 定义

```csharp
var ordersAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new ChatOptions
    {
        Instructions = """
            你是 VoltCart 订单智能体。
            帮助客户查询订单、物流状态、取消订单。
            简洁友好,操作前必须确认订单号。
            取消订单前必须先查订单状态。
        """,
        Tools = [
            AIFunctionFactory.Create(OrderTools.LookupOrder),
            AIFunctionFactory.Create(OrderTools.GetOrderStatus),
            AIFunctionFactory.Create(OrderTools.CancelOrder)
        ]
    },
    Name = "ordersAgent"
})
.AsBuilder()
.UseOpenTelemetry("voltcartsupport")
.Build();
```

### 5.2 它能调用的 3 个工具

```csharp
public static class OrderTools
{
    /// 查订单详情
    [Description("根据订单号查询订单详情")]
    public static string LookupOrder(
        [Description("订单号,例如 ORD-1234")] string orderId)
    {
        var orders = new Dictionary<string, string>
        {
            ["ORD-1001"] = "订单 ORD-1001: 无线耳机 ($79.99),已送达",
            ["ORD-1002"] = "订单 ORD-1002: 智能音箱 ($129.99)+ USB-C 线 ($12.99),运输中",
            ["ORD-1003"] = "订单 ORD-1003: 笔记本支架 ($49.99),处理中"
        };
        return orders.TryGetValue(orderId, out var info) ? info : "未找到订单";
    }

    /// 查物流状态
    [Description("获取订单的当前物流状态")]
    public static string GetOrderStatus(
        [Description("要查询的订单号")] string orderId) { /* ... */ }

    /// 取消订单（带确认校验）
    [Description("取消订单。仅适用于未发货订单。重要：调用前必须先与客户确认")]
    public static string CancelOrder(
        [Description("要取消的订单号")] string orderId,
        [Description("必须是 'true' - 客户必须明确确认取消")] string customerConfirmed)
    {
        if (!customerConfirmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            return "未确认取消。请先询问客户是否确认取消。";

        var cancellable = new HashSet<string> { "ORD-1003" };
        return cancellable.Contains(orderId)
            ? $"订单 {orderId} 已成功取消,确认邮件即将发出"
            : $"订单 {orderId} 无法取消 - 已发货或已送达";
    }
}
```

> 🛡️ **设计小心机**：`CancelOrder` 多了一个 `customerConfirmed` 参数，**强制 AI 必须在取消前先和用户二次确认**——这就是"AI 时代的安全编程"，把业务约束写进工具签名里。

### 5.3 典型交互流程

```
👤 用户: "我的 ORD-1002 怎么还没到？"
   ↓
🤖 ordersAgent 思考: 需要查物流状态
   ↓ → 调用 GetOrderStatus("ORD-1002")
   ↓ ← "运输中,预计 3 月 11 日送达"
   ↓
🤖 ordersAgent: "您的订单 ORD-1002 正在运输中,预计 3 月 11 日送达,请耐心等待 🙏"
```

---

## 💰 六、第三个智能体：billingAgent（账单智能体）

**职责**：查发票、处理退款、查支付历史。

### 6.1 定义

```csharp
var billingAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new ChatOptions
    {
        Instructions = """
            你是 VoltCart 账单智能体。
            帮助客户处理发票和支付历史。
            退款请求需说明 VoltCart 在提交前会走内部确认流程。
            保持同理心。
        """,
        Tools = [
            AIFunctionFactory.Create(BillingTools.GetInvoice),
            AIFunctionFactory.Create(BillingTools.ProcessRefund),
            AIFunctionFactory.Create(BillingTools.GetPaymentHistory)
        ]
    },
    Name = "billingAgent"
})
.AsBuilder()
.UseOpenTelemetry("voltcartsupport")
.Build();
```

### 6.2 它能调用的 3 个工具

```csharp
public static class BillingTools
{
    /// 查发票
    [Description("根据订单号检索发票")]
    public static string GetInvoice(
        [Description("订单号")] string orderId) { /* 返回发票明细 */ }

    /// 处理退款
    [Description("在获得批准后处理退款")]
    public static string ProcessRefund(
        [Description("要退款的订单号")] string orderId,
        [Description("退款原因")] string reason)
        => $"订单 {orderId} 的退款已发起。原因:{reason}。5-10 个工作日到账。";

    /// 查支付历史
    [Description("获取当前客户的支付历史")]
    public static string GetPaymentHistory() => """
        最近支付:
        - 3 月 1 日:$86.39 (ORD-1001,Visa 尾号 4242)
        - 3 月 3 日:$154.42 (ORD-1002,PayPal)
        - 3 月 5 日:$53.99 (ORD-1003,Visa 尾号 1234)
        """;
}
```

### 6.3 它与 ordersAgent 的协作

```
👤 用户: "ORD-1001 想退款"
   ↓
🤖 billingAgent 思考: 退款前应先核实订单
   ↓ → 调用 GetInvoice("ORD-1001")   // 跨工具协作:查账单
   ↓ ← "发票 INV-5001:无线耳机 $79.99..."
   ↓
🤖 billingAgent: "您 ORD-1001 的发票金额是 $86.39。
                 请告诉我退款原因,我将为您提交申请。
                 ⚠️ 根据 VoltCart 流程,提交前会由专人复核。"
```

> 🎯 **亮点**：这个 Agent 没有"查订单"的工具，但它通过 AI 的推理能力，依然能为客户提供完整的退款上下文。**这就是 LLM Agent 相比传统 RPA 的最大优势——模糊决策能力**。

---

## 🛠️ 七、第四个智能体：technicalAgent（技术智能体）

**职责**：搜知识库解决常见问题，搞不定就建工单。

### 7.1 定义

```csharp
var technicalAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new ChatOptions
    {
        Instructions = """
            你是 VoltCart 技术支持智能体。
            先搜索知识库,再考虑创建工单。
            一步步引导客户排查。
        """,
        Tools = [
            AIFunctionFactory.Create(TechnicalTools.CreateSupportTicket),
            AIFunctionFactory.Create(TechnicalTools.SearchKnowledgeBase)
        ]
    },
    Name = "technicalAgent"
})
.AsBuilder()
.UseOpenTelemetry("voltcartsupport")
.Build();
```

### 7.2 它能调用的 2 个工具

```csharp
public static class TechnicalTools
{
    /// 搜知识库（KB-101/102/103 三篇文章）
    [Description("搜索 VoltCart 产品知识库的故障排除信息")]
    public static string SearchKnowledgeBase(
        [Description("搜索关键词,例如 'smart speaker wifi setup'")] string query)
    {
        var articles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["wifi"] = """
                KB-101: 智能音箱 WiFi 设置
                1. 长按 reset 键 5 秒
                2. 打开 VoltCart App → 设置 → 设备 → 添加新设备
                3. 选择 WiFi 并输入密码
                4. 等待音箱发出提示音
                ⚠️ 仅支持 2.4GHz,不支持 5GHz
                """,
            ["headphones"] = "KB-102: 无线耳机故障排除...",
            ["laptop stand"] = "KB-103: 笔记本支架组装..."
        };

        var match = articles.FirstOrDefault(a =>
            query.Contains(a.Key, StringComparison.OrdinalIgnoreCase));
        return match.Value ?? $"未找到 '{query}' 的相关文章,建议建工单。";
    }

    /// 建工单
    [Description("为无法通过排查解决的问题创建支持工单")]
    public static string CreateSupportTicket(
        [Description("问题简述")] string issueDescription,
        [Description("涉及的产品,如 smart speaker")] string? product = null)
    {
        var ticketId = $"TKT-{Random.Shared.Next(10000, 99999)}";
        return $"工单 {ticketId} 已创建。问题:{issueDescription}。{product}。"
             + "工程师将在 24 小时内跟进。";
    }
}
```

### 7.3 典型交互：RAG 的雏形

```
👤 用户: "智能音箱连不上 WiFi"
   ↓
🤖 technicalAgent 思考: 关键词 "wifi" 命中 KB-101
   ↓ → 调用 SearchKnowledgeBase("smart speaker wifi")
   ↓ ← 返回 KB-101 全文
   ↓
🤖 technicalAgent:
   "请按以下步骤操作:
    1️⃣ 长按音箱背面 reset 键 5 秒
    2️⃣ 打开 VoltCart App → 设置 → 设备 → 添加新设备
    3️⃣ 选择 WiFi 输入密码
    4️⃣ 等待提示音
    ⚠️ 仅支持 2.4GHz
    
    如果还不行,我帮您建个工单,让工程师 24h 内联系您?"
```

> 📚 这其实就是一个**最小可行的 RAG（检索增强生成）系统**——真实场景里把 `SearchKnowledgeBase` 换成向量数据库（Qdrant / Azure AI Search）就能秒变企业级知识库。

---

## 🔗 八、Workflow：把智能体串成流水线

光有智能体还不够,我们需要**让它们协作**——这就是 Workflow 登场的时候。

### 8.1 三个 Executor：工作流的"积木块"

#### ① TriageDecisionExecutor：JSON 解析器

```csharp
public class TriageDecisionExecutor()
    : Executor<List<ChatMessage>, TriageDecision>("parser-triage")
{
    public override async ValueTask<TriageDecision> HandleAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // 拿到 triageAgent 最后一条消息的文本,反序列化为强类型
        var json = messages.LastOrDefault(x => x.Role == ChatRole.Assistant)?.Text;
        return JsonSerializer.Deserialize<TriageDecision>(json)
            ?? throw new InvalidOperationException(
                $"Triage response was not valid json: {json}");
    }
}
```

> 💡 为什么要单独搞个 Executor？因为 triageAgent 输出的是 `List<ChatMessage>`（字符串），而下游需要的是 `TriageDecision`（对象）。这个 Executor 就是"翻译官"。

#### ② SpeciallistExecutor：智能体的执行壳

```csharp
public class SpeciallistExecutor : Executor<TriageDecision, ChatMessage>
{
    private readonly AIAgent _agent;
    public SpeciallistExecutor(string id, AIAgent agent) : base(id)
        => _agent = agent;

    public override async ValueTask<ChatMessage> HandleAsync(
        TriageDecision message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // 拿到分诊摘要,丢给智能体
        var response = await _agent.RunAsync(message.Summary, cancellationToken: cancellationToken);
        return response.Messages.LastOrDefault(x => x.Role == ChatRole.Assistant);
    }
}
```

> 💡 `Executor<TIn, TOut>` 的泛型签名就是工作流的"接口契约"——上游传 `TIn`，下游收 `TOut`，类型安全。

### 8.2 编排：用 WorkflowBuilder 画流程图

```csharp
builder.AddWorkflow("TriagleSupport", (sp, key) =>
{
    var triage     = sp.GetRequiredKeyedService<AIAgent>("triageAgent");
    var orders     = sp.GetRequiredKeyedService<AIAgent>("ordersAgent");
    var billing    = sp.GetRequiredKeyedService<AIAgent>("billingAgent");
    var technical  = sp.GetRequiredKeyedService<AIAgent>("technicalAgent");

    // 把 Agent 包成 Executor
    var triageBinding = triage.BindAsExecutor(
        new AIAgentHostOptions { ForwardIncomingMessages = false });
    var parser        = new TriageDecisionExecutor();
    var ordersRun     = new SpeciallistExecutor("orders-run",     orders);
    var billingRun    = new SpeciallistExecutor("billing-run",    billing);
    var technicalRun  = new SpeciallistExecutor("technical-run",  technical);

    // 用 Builder 把节点连起来
    var workflow = new WorkflowBuilder(triageBinding)
        .WithName("TriagleSupport")
        // 用户消息 → 分诊
        .AddEdge(triageBinding, parser)
        // 分诊结果 → 条件路由到对应智能体
        .AddEdge(parser, ordersRun,    GetCondition("orders"))
        .AddEdge(parser, billingRun,   GetCondition("billing"))
        .AddEdge(parser, technicalRun, GetCondition("technical"))
        .WithOutputFrom(ordersRun, billingRun, technicalRun)
        .WithOpenTelemetry(activitySource: activitySource,
                           configure: c => c.EnableSensitiveData = true)
        .Build();

    return workflow;
});

static Func<object?, bool> GetCondition(string agentName)
    => result => result is TriageDecision d && d.TargetAgent == agentName;
```

### 8.3 完整流程图

```
                         用户消息
                            │
                            ▼
                  ┌──────────────────┐
                  │   triageBinding  │  ← triageAgent
                  │  (AI: 读懂问题)  │
                  └────────┬─────────┘
                           │ List<ChatMessage>
                           ▼
                  ┌──────────────────┐
                  │      parser      │  ← TriageDecisionExecutor
                  │ (JSON → 对象)    │
                  └────────┬─────────┘
                           │ TriageDecision
        ┌──────────────────┼──────────────────┐
        │ targetAgent=?    │ targetAgent=?    │ targetAgent=?
        ▼ "orders"        ▼ "billing"        ▼ "technical"
┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│ ordersRun    │   │ billingRun   │   │ technicalRun │
│ (ordersAgent)│   │(billingAgent)│   │(technicalAgent)│
└──────┬───────┘   └──────┬───────┘   └──────┬───────┘
       │                  │                  │
       └──────────────────┼──────────────────┘
                          ▼
                     ChatMessage
                      (给用户)
```

### 8.4 Workflow 的 4 个核心概念

| 概念 | 类比 | 作用 |
|---|---|---|
| **Executor** | 流水线工位 | 处理一个输入，产生一个输出 |
| **Edge** | 流水线传送带 | 连接两个工位 |
| **Condition** | 分流器 | 根据条件决定消息走哪条 Edge |
| **WorkflowBuilder** | 流程图编辑器 | 把节点和边拼成完整流程 |

> 🎓 这种**"图状编排"** 比 if-else 写死业务逻辑灵活太多——以后想加个"升级到主管"的 Edge，只需要在 Builder 里加一行。

---

## 🛡️ 九、安全护栏：Guardrail Middleware

Agent 虽强，但**没有护栏的 Agent = 定时炸弹**。

### 9.1 输入过滤 + 输出截断

```csharp
public static class InputSanitation
{
    public static async Task<AgentResponse> GuardraiMiddleware(
        IEnumerable<ChatMessage> messages, AgentSession? session,
        AgentRunOptions? options, AIAgent innerAgent,
        CancellationToken cancellationToken)
    {
        // ① 输入侧：检测敏感词
        var lastText = messages.LastOrDefault().Text.ToLower();
        string[] blockedWords = ["password", "secret", "credentials"];

        foreach (var word in blockedWords)
        {
            if (lastText.Contains(word))
            {
                Console.WriteLine($"[GUARDRAIL] 拦截包含 '{word}' 的请求");
                return new AgentResponse(new ChatMessage(
                    ChatRole.Assistant,
                    $"抱歉,我无法处理涉及 '{word}' 的请求。"));
            }
        }

        // ② 正常处理
        var response = await innerAgent.RunAsync(session, options, cancellationToken);

        // ③ 输出侧：超长截断
        var outputText = response.Messages.Last().Text;
        if (outputText.Length > 5000)
        {
            Console.WriteLine($"[GUARDRAIL] 响应过长,截断");
            return new AgentResponse(new ChatMessage(
                ChatRole.Assistant, outputText[..5000]));
        }

        return response;
    }
}
```

### 9.2 挂载到 Agent

```csharp
var triageAgent = chatClient.AsAIAgent(...)
    .AsBuilder()
    .Use(runFunc: InputSanitation.GuardraiMiddleware)
    .Build();
```

> 🔒 **生产环境必做三件事**：① 敏感词过滤 ② PII 脱敏（手机号、身份证、邮箱）③ 输出长度限制。这个中间件就是最佳范式。

---

## 📊 十、可观测性：OpenTelemetry 全链路追踪

每个 Agent 和 Workflow 都挂了 OTel 追踪：

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("voltcartsupport"))
    .WithTracing(t =>
    {
        t.AddSource("voltcartsupport")
         .AddSource("Experimental.Microsoft.Agents.AI")
         .AddSource("Experimental.Microsoft.Agents.AI.Workflow")
         .AddSource("Experimental.Microsoft.Agents.AI.*")
         .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317"))
         .AddConsoleExporter();      // 控制台也能看
    });
```

启动后你能在控制台看到类似这样的追踪树：

```
triageAgent.Run (1240ms)
  ├─ chat gpt-4o-mini (1100ms, 230 prompt + 80 completion tokens)
  └─ tool: GetCustomerInfo (45ms)

ordersAgent.Run (890ms)
  ├─ chat gpt-4o-mini (520ms)
  └─ tool: GetOrderStatus (12ms)
```

**能看见什么**：
- 每个 Agent 调用耗时
- Token 消耗
- 工具调用链路
- 异常堆栈

**对接 Jaeger / Tempo / Datadog**：把 OTLP Endpoint 改成你的 collector 地址即可。

---

## 🖥️ 十一、DevUI：可视化调试界面

```csharp
builder.AddDevUI();
app.MapDevUI();
```

启动 `dotnet run`，浏览器打开 `http://localhost:5000/devui`，你会看到：

> 💬 左侧聊天窗口（直接和 Agent 对话）
> 🔍 右侧 Trace 面板（看每一步工具调用、Token 消耗、耗时）
> 🎛️ 顶部可以切换不同 Agent 测试

这是我见过**最丝滑的 AI 调试体验**——比 LangChain 的 LangSmith 还要直观。

> （此处建议你放一张 DevUI 的真实截图）

---

## 📦 十二、NuGet 包一览

```xml
<PackageReference Include="Microsoft.Agents.AI.DevUI"      Version="1.21.0-preview" />
<PackageReference Include="Microsoft.Agents.AI.OpenAI"     Version="1.21.0" />
<PackageReference Include="Microsoft.Agents.AI.Workflows"   Version="1.21.0" />
<PackageReference Include="Microsoft.AspNetCore.OpenApi"    Version="10.0.9" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.18.0" />
```

**运行环境要求**：**.NET 10**

---

## 🚀 十三、运行效果

```bash
dotnet run
```

打开 `http://localhost:5000/devui`，试试这些对话：

| 用户输入 | 路由结果 | 调用的工具 |
|---|---|---|
| "我的订单 ORD-1002 怎么还没到？" | `ordersAgent` | `GetOrderStatus` |
| "我想取消订单 ORD-1003" | `ordersAgent` | `CancelOrder`（带确认） |
| "ORD-1001 想退款" | `billingAgent` | `GetInvoice` → `ProcessRefund` |
| "音箱连不上 WiFi" | `technicalAgent` | `SearchKnowledgeBase("wifi")` |
| "我的密码忘了" | 🚫 护栏拦截 | 返回友好拒绝 |

每个 Agent 的工具调用、思考过程、Token 消耗，在 DevUI 里**一览无余**。

---

## 🎯 十四、给 .NET 同学的总结

如果你一直觉得 AI 开发是 Python 的专属，那这个项目会刷新你的认知：

| 你以为的痛点 | .NET 现在的解法 |
|---|---|
| AI 库都是 Python 的 | `Microsoft.Agents.AI` 原生 C# SDK |
| 类型不安全 | `ResponseFormat.ForJsonSchema<T>()` 强类型 |
| 调试 AI 像开盲盒 | `AddDevUI()` 可视化调试 |
| 工具注册繁琐 | `[Description]` 一行搞定 |
| 多 Agent 编排复杂 | `WorkflowBuilder` 图状编排 |
| 缺乏可观测性 | 一行 `.UseOpenTelemetry()` 全链路追踪 |
| 安全问题没人管 | `.Use()` 中间件机制 |

### 下一步可以做的事

1️⃣ **替换真实数据源**：`OrderTools` 的 Dictionary 换成 EF Core / Dapper 查数据库

2️⃣ **接入 RAG**：用 `Microsoft.Extensions.VectorStorage` + `SearchKnowledgeBase` 接入向量数据库

3️⃣ **强化中间件**：用 `Microsoft.Extensions.AI` 的 `FunctionInvocationFilter` 实现日志、限流、重试

4️⃣ **多轮对话**：用 `AddOpenAIConversations()` 自动管理会话上下文

5️⃣ **生产化部署**：把 OTLP Endpoint 指向 Jaeger / Tempo，加鉴权 + 限流

---

## 💬 写在最后

2026 年了，**不会用 AI 写代码的程序员，可能真的会掉队**——但更可怕的是，**不会用 AI 框架搭系统的程序员，连转型的机会都没有**。

.NET 在 AI Agent 领域不再是"二等公民"。这次 Microsoft Agent Framework 的设计，让我看到了 .NET 生态在 AI 时代重新崛起的可能。

> 你觉得 .NET 做 AI 开发的体验怎么样？欢迎评论区聊聊 👇

如果觉得有帮助，**点赞 + 在看 + 转发** 就是对我最大的支持 🙌

---

### 📎 文末小福利

- 项目源码：[放你的 GitHub 链接]
- Microsoft Agent Framework 官方文档：https://learn.microsoft.com/en-us/agent-framework/
- 配套视频教程：[B站/YouTube 链接]

---

## ✍️ 文章使用说明

**直接复制建议**：

1. 微信公众号编辑器粘贴后，开头加个**关注引导图**（"👇 完整代码 + 项目源码在文末"）
2. 第七节 DevUI 部分替换成你的**真实截图**
3. 第十三节的表格可以做成图片，公众号对 Markdown 表格支持一般
4. 代码块语法高亮公众号不会保留，建议在编辑器里手动选 C# 主题
5. 文末链接替换成你自己的 GitHub / B 站地址
6. 封面图建议用 VSCode + Agent 流程图风格，配色蓝紫调

**标题备选**：

- 《.NET 10 杀疯了！一行代码搞定 AI Agent，Python 看了沉默》
- 《微软终于把 AI Agent 做进了 .NET,我通宵肝了一个真实项目》
- 《C# 也能写 AI Agent 了！手把手带你跑通 4 Agent 协作工作流》
- 《从0到1,我用 .NET 10 + Agent Framework 搭了一个能"干活"的AI客服》

**预估阅读时间**：12~15 分钟
**预估字数**：约 4500 字
**适合人群**：.NET 开发者、对 AI Agent 感兴趣的程序员、技术管理者
