# GitHub Copilot Instructions для InvoiceAI

## 🎯 О ПРОЕКТЕ

**InvoiceAI (СЧЁТАИ)** — микро-SaaS для фрилансеров РФ/СНГ, генерирующий инвойсы через AI-парсинг текста.

**Ключевая фича:** Фрилансер пишет "Редизайн для ООО Ромашка, 80к, 50% предоплата, до 25 декабря, Иван [ivan@mail.com](mailto:ivan@mail.com)" → за 10 сек готовый PDF + email клиенту.

---

## Important: Share context with agent-shaker of each enchancements and improvements. If you create MD document share context to agent-shaker too.

## 🏗️ TECH STACK

### Backend

* **Framework:** [ASP.NET](http://ASP.NET) Core 10 (Minimal API + Controllers)

* **Database:** PostgreSQL 15+ + Entity Framework Core

* **Authentication:** JWT Bearer + Refresh Tokens

* **AI:** Azure OpenAI (GPT-4) для парсинга текста

* **PDF:** GemBox.Document (DOCX → PDF)

* **Email:** GemBox.Email (SMTP)

* **Payments:** YooKassa (только РФ)

* **Background Jobs:** Hangfire (для auto-reminders в будущем)

### Architecture

```
InvoiceAI/
├── InvoiceAI.Domain/              # Entities, Enums
│   ├── Entities/
│   │   ├── User.cs
│   │   ├── Client.cs
│   │   ├── Invoice.cs
│   │   ├── InvoiceItem.cs
│   │   ├── Subscription.cs
│   │   ├── SubscriptionPlan.cs
│   │   ├── Payment.cs
│   │   ├── InvoiceTemplate.cs
│   │   └── AuditLog.cs
│   └── Enums/
│       └── InvoiceStatus.cs
│
├── InvoiceAI.Application/         # DTOs, Interfaces
│   ├── DTOs/
│   │   ├── Auth/
│   │   ├── AI/
│   │   ├── Invoice/
│   │   ├── Client/
│   │   ├── Dashboard/
│   │   └── Payment/
│   └── Interfaces/
│       ├── IAiInvoiceService.cs
│       ├── IInvoiceService.cs
│       ├── IClientService.cs
│       ├── IInvoicePdfGenerator.cs
│       ├── IEmailSender.cs
│       ├── IAuthService.cs
│       ├── ITokenService.cs
│       ├── IPasswordHasher.cs
│       └── IPaymentService.cs
│
├── InvoiceAI.Infrastructure/      # Implementations
│   ├── Data/
│   │   ├── ApplicationDbContext.cs
│   │   └── DbSeeder.cs
│   └── Services/
│       ├── AiInvoiceService.cs
│       ├── InvoiceService.cs
│       ├── ClientService.cs
│       ├── InvoicePdfGenerator.cs
│       ├── EmailSender.cs
│       ├── AuthService.cs
│       ├── TokenService.cs
│       ├── PasswordHasher.cs
│       └── PaymentService.cs
│
└── InvoiceAI.API/                 # Controllers, Program.cs
    ├── Controllers/
    │   ├── AuthController.cs
    │   ├── AiController.cs
    │   ├── InvoicesController.cs
    │   ├── ClientsController.cs
    │   ├── DashboardController.cs
    │   └── PaymentsController.cs
    ├── Program.cs
    └── appsettings.json
```

---

## 📊 DOMAIN MODEL

### User

```csharp
public class User
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public string FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation
    public Subscription Subscription { get; set; }
    public ICollection<Client> Clients { get; set; }
    public ICollection<Invoice> Invoices { get; set; }
}
```

### Client

```csharp
public class Client
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? TaxId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation
    public User User { get; set; }
    public ICollection<Invoice> Invoices { get; set; }
}
```

### Invoice

```csharp
public class Invoice
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ClientId { get; set; }
    public string InvoiceNumber { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidDate { get; set; }
    public bool IsSent { get; set; }
    public DateTime? SentDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation
    public User User { get; set; }
    public Client Client { get; set; }
    public ICollection<InvoiceItem> Items { get; set; }
}
```

### InvoiceItem

```csharp
public class InvoiceItem
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string Description { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    
    // Navigation
    public Invoice Invoice { get; set; }
}
```

### Subscription

```csharp
public class Subscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int PlanId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    
    // Navigation
    public User User { get; set; }
    public SubscriptionPlan Plan { get; set; }
}
```

### SubscriptionPlan

```csharp
public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Name { get; set; } // Free, Pro, Team
    public decimal Price { get; set; }
    public int MaxInvoicesPerMonth { get; set; }
    public int MaxClients { get; set; }
    public string Features { get; set; }
}
```

### Payment

```csharp
public class Payment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int SubscriptionId { get; set; }
    public string PaymentId { get; set; } // YooKassa payment ID
    public string Provider { get; set; } // "yookassa"
    public string Status { get; set; } // pending, succeeded, canceled
    public decimal Amount { get; set; }
    public string Currency { get; set; } // RUB
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Navigation
    public User User { get; set; }
    public Subscription Subscription { get; set; }
}
```

---

## 🎨 CODING STYLE

### General

* **Language:** C# 12 (.NET 10)

* **Naming:** PascalCase для public, camelCase для private

* **Async:** Всегда используй `async/await` для I/O операций

* **Cancellation:** Всегда добавляй `CancellationToken cancellationToken = default`

* **Logging:** Используй `ILogger<T>` для всех сервисов

* **Null Safety:** Используй nullable reference types (`string?`)

### Controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class ExampleController : ControllerBase
{
    private readonly IExampleService _service;
    private readonly ILogger<ExampleController> _logger;

    public ExampleController(IExampleService service, ILogger<ExampleController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// XML комментарий для Swagger
    /// </summary>
    [Authorize]
    [HttpGet("{id}")]
    public async Task<ActionResult<ExampleResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _service.GetByIdAsync(userId, id, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting example {Id}", id);
            return BadRequest(new { error = ex.Message });
        }
    }
}
```

### Services

```csharp
public class ExampleService : IExampleService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ExampleService> _logger;

    public ExampleService(ApplicationDbContext context, ILogger<ExampleService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ExampleResponse> GetByIdAsync(
        int userId,
        int id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Examples
            .Where(e => e.UserId == userId && e.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity == null)
        {
            throw new Exception("Example not found");
        }

        return new ExampleResponse
        {
            Id = entity.Id,
            Name = entity.Name
        };
    }
}
```

### DTOs

```csharp
// Request DTOs
public record CreateExampleRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

// Response DTOs
public record ExampleResponse
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
```

---

## 🔐 SECURITY

### Authentication

* JWT Bearer tokens (1 hour expiry)

* Refresh tokens (7 days expiry)

* PBKDF2 для хеширования паролей (100,000 iterations)

### Authorization

* Все endpoints (кроме `/auth/register`, `/auth/login`, `/auth/refresh`) требуют `[Authorize]`

* Всегда проверяй `userId` из JWT перед доступом к данным

### Rate Limiting

* Global: 100 requests/minute per IP

* Auth endpoints: 10 requests/minute per IP

---

## 💳 PAYMENTS (YooKassa)

### Flow

1. User → `POST /api/payments/create-checkout { planId: 2 }`

2. Backend создаёт платёж в YooKassa

3. Backend возвращает `checkoutUrl`

4. Frontend редиректит на `checkoutUrl`

5. User оплачивает

6. YooKassa → webhook → `POST /api/payments/webhook/yookassa`

7. Backend обновляет подписку

8. YooKassa редиректит на `returnUrl`

### Subscription Plans

* **Free:** 0₽, 10 invoices/month, 5 clients

* **Pro:** 490₽/month, 100 invoices/month, 50 clients

* **Team:** 990₽/month, 500 invoices/month, 200 clients

---

## 🤖 AI PARSING

### Prompt Template

```csharp
var prompt = $@"
Ты — ассистент для парсинга текста в структурированный инвойс.

Текст от пользователя:
""{text}""

Извлеки из текста:
1. Клиент (название, email, телефон, адрес)
2. Услуги/товары (описание, количество, цена)
3. Общая сумма
4. Срок оплаты (дедлайн)
5. Примечания

Верни JSON:
{{
  ""client"": {{
    ""name"": ""ООО Ромашка"",
    ""email"": ""ivan@romashka.ru"",
    ""phone"": null,
    ""address"": null
  }},
  ""items"": [
    {{
      ""description"": ""Редизайн сайта"",
      ""quantity"": 1,
      ""unitPrice"": 80000,
      ""amount"": 80000
    }}
  ],
  ""total"": 80000,
  ""dueDate"": ""2025-12-25T00:00:00Z"",
  ""notes"": null
}}

Если информация отсутствует — используй null.
Если указана предоплата (например, 50%) — раздели на 2 позиции.
";
```

---

## 📄 PDF GENERATION (GemBox.Document)

### Template

* DOCX шаблон: `Templates/InvoiceTemplate.docx`

* Плейсхолдеры: `{{InvoiceNumber}}`, `{{ClientName}}`, `{{TotalAmount}}`, etc.

* GemBox заменяет плейсхолдеры → сохраняет как PDF

### Example

```csharp
var document = DocumentModel.Load("Templates/InvoiceTemplate.docx");

document.Content.Replace("{{InvoiceNumber}}", invoice.InvoiceNumber);
document.Content.Replace("{{ClientName}}", invoice.Client.Name);
document.Content.Replace("{{TotalAmount}}", invoice.TotalAmount.ToString("N2"));

using var stream = new MemoryStream();
document.Save(stream, new PdfSaveOptions());
return stream.ToArray();
```

---

## 📧 EMAIL (GemBox.Email)

### Template

```csharp
var message = new MailMessage(
    new MailAddress(senderEmail, senderName),
    new MailAddress(recipientEmail))
{
    Subject = $"Счёт {invoice.InvoiceNumber} от {invoice.IssueDate:dd.MM.yyyy}",
    BodyText = $@"
Добрый день, {invoice.Client.Name}!

{customMessage}

Во вложении счёт {invoice.InvoiceNumber} на сумму {invoice.TotalAmount:N2} ₽.
Срок оплаты: {invoice.DueDate:dd.MM.yyyy}.

С уважением,
{user.FullName}
СЧЁТАИ
"
};

message.Attachments.Add(new Attachment(pdfStream, $"Invoice_{invoice.InvoiceNumber}.pdf"));

using var smtp = new SmtpClient(smtpHost, smtpPort);
smtp.Authenticate(smtpUsername, smtpPassword);
smtp.SendMessage(message);
```

---

## 📊 DASHBOARD

### Stats Endpoint

`GET /api/dashboard/stats` возвращает:

* **Financial:** totalRevenue, paidAmount, pendingAmount, overdueAmount, revenueGrowth

* **Invoices:** totalInvoices, paidInvoices, pendingInvoices, overdueInvoices

* **Subscription:** planName, invoicesUsed, invoicesLimit, clientsUsed, clientsLimit

* **Recent Invoices:** последние 5 инвойсов

* **Monthly Revenue:** revenue по месяцам (12 месяцев)

---

## 🧪 TESTING

### Swagger UI

* URL: `/api/docs`

* JWT Authorization: Click "Authorize" → `Bearer {token}`

### Postman

* Import: `InvoiceAI.postman_collection.json`

* Variables: `base_url`, `access_token`, `refresh_token`

* Auto-save token: Test script в Login request

---

## 🚀 DEPLOYMENT

### Environment Variables

```bash
ConnectionStrings__DefaultConnection="Host=...;Database=invoiceai;..."
JWT__SecretKey="your-256-bit-secret"
AzureAI__Endpoint="https://..."
AzureAI__ApiKey="..."
YooKassa__ShopId="..."
YooKassa__SecretKey="..."
Smtp__Host="smtp.gmail.com"
Smtp__Username="..."
Smtp__Password="..."
```

### Platforms

* **Railway:** `railway up`

* **Render:** Connect GitHub → Deploy

* **Docker:** `docker build -t invoiceai .`

---

## 🎯 BUSINESS LOGIC

### Subscription Limits

* При создании инвойса проверяй `invoicesThisMonth < plan.MaxInvoicesPerMonth`

* При создании клиента проверяй `clientsCount < plan.MaxClients`

* Если лимит превышен → `throw new Exception("Subscription limit exceeded")`

### Invoice Number Generation

* Format: `INV-{year}-{sequential}`

* Example: `INV-2025-0001`, `INV-2025-0002`

* Sequential per user per year

### Auto-Reminders (Future)

* Hangfire job: каждый день в 10:00 МСК

* Проверяет overdue invoices

* Отправляет email reminder

---

## 📝 CONVENTIONS

### Commit Messages

```
feat: Add AI invoice parsing
fix: Fix subscription limit check
docs: Update API reference
refactor: Simplify payment service
test: Add invoice service tests
```

### Branch Naming

```
feature/ai-parsing
bugfix/subscription-limit
hotfix/payment-webhook
```

### PR Template

```markdown
## Description
Brief description of changes

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Testing
- [ ] Tested locally
- [ ] Tested on staging
- [ ] Added unit tests

## Checklist
- [ ] Code follows style guidelines
- [ ] Self-review completed
- [ ] Documentation updated
```

---

## 🔧 COMMON TASKS

### Add New Endpoint

1. Create DTO in `Application/DTOs/`

2. Add method to interface in `Application/Interfaces/`

3. Implement in service in `Infrastructure/Services/`

4. Add controller action in `API/Controllers/`

5. Add XML comment for Swagger

6. Test in Swagger UI

7. Update Postman collection

### Add New Entity

1. Create entity in `Domain/Entities/`

2. Add `DbSet` to `ApplicationDbContext`

3. Configure in `OnModelCreating`

4. Create migration: `dotnet ef migrations add AddEntityName`

5. Apply migration: `dotnet ef database update`

### Add New Service

1. Create interface in `Application/Interfaces/`

2. Implement in `Infrastructure/Services/`

3. Register in `Program.cs`: `builder.Services.AddScoped<IService, Service>()`

---

## ⚠️ ВАЖНО

### Никогда не делай:

* ❌ Не используй Stripe (только YooKassa)

* ❌ Не используй QuestPDF (только GemBox.Document)

* ❌ Не используй MailKit (только GemBox.Email)

* ❌ Не возвращай пароли в API responses

* ❌ Не логируй sensitive data (пароли, токены, API keys)

* ❌ Не забывай про `[Authorize]` на protected endpoints

* ❌ Не забывай проверять `userId` перед доступом к данным

### Всегда делай:

* ✅ Используй `async/await` для I/O

* ✅ Добавляй `CancellationToken`

* ✅ Логируй ошибки через `ILogger`

* ✅ Используй `record` для DTOs

* ✅ Добавляй XML комментарии для Swagger

* ✅ Проверяй subscription limits

* ✅ Валидируй input данные

* ✅ Используй transactions для критичных операций

---

## 📚 REFERENCES

* **API Docs:** `/api/docs` (Swagger UI)

* **API Reference:** `API_REFERENCE.md`

* **Postman:** `InvoiceAI.postman_collection.json`

* **README:** `README.md`

---

## 🎯 MVP SCOPE

**v1.0 (Current):**

* ✅ Auth (JWT + Refresh)

* ✅ AI-парсинг текста → InvoiceDraft

* ✅ CRUD для Clients

* ✅ CRUD для Invoices

* ✅ PDF generation (GemBox)

* ✅ Email sending (GemBox)

* ✅ Dashboard stats

* ✅ YooKassa payments

* ✅ Subscription management

**v1.1 (Next):**

* ⏳ Auto-reminders (Hangfire)

* ⏳ Invoice templates (custom)

* ⏳ Multi-currency support

* ⏳ Export to Excel

**v2.0 (Future):**

* ⏳ Team collaboration

* ⏳ API access

* ⏳ Webhooks

* ⏳ Mobile app

---

## 💡 TIPS

### Performance

* Используй `.AsNoTracking()` для read-only queries

* Используй `.Include()` для eager loading

* Добавляй индексы на часто используемые поля

### Security

* Всегда валидируй input

* Используй parameterized queries (EF Core делает это автоматически)

* Не доверяй client-side данным

### Debugging

* Используй `_logger.LogInformation()` для важных событий

* Используй `_logger.LogError(ex, ...)` для ошибок

* Проверяй Swagger UI для быстрого тестирования

## Agent Shaker MCP

# Agent Identity and MCP Integration

## Agent Shaker MCP Your Identity
- **Agent Name**: Global API Backend Development
- **Agent ID**: 78b9f3db-7ee2-43c3-9486-ef694d0c2a12
- **Role**: backend
- **Team**: RND
- **Project**: СчетAI
- **Project ID**: 1a25f34c-7ea3-4453-bd7c-bc42e8306ab2

## Agent Shaker MCP Project Identity agents

- **Agent Name**: Global Frontend Development
- **Agent ID**: 7b800c8e-c641-4222-8e2f-5b6f0a04c5af
- **Role**: frontend
- **Team**: RND
- **Project**: СчетAI
- **Project ID**: 1a25f34c-7ea3-4453-bd7c-bc42e8306ab2


### Agent Shaker MCP Your Responsibility

- Push markdown context and all md documents in solution and create tags
- Create tasks across project agents by role to be share a tasks across implementation and other agents
- Focus on API development and backend logic
- Work with databases and data models
- Implement business logic and validations
- Handle server-side security and authentication

---

**Версия:** 1.0\
**Последнее обновление:** 2025-12-17\
**Автор:** InvoiceAI Team