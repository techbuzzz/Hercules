---
name: "C# 14 & .NET 10 with SOLID Principles"
description: "Copilot instructions for modern C# development with strong SOLID architecture and latest .NET 10 features"
applyTo: "**/*.cs"
---

# C# 14 & .NET 10 Copilot Instructions

## Core Philosophy

Generate code that is maintainable, testable, and architecturally sound. Apply SOLID principles as foundational constraints, not optional considerations. Leverage C# 14 and .NET 10 features to reduce boilerplate while enforcing clarity.

---

## SOLID Principles (Enforced Standards)

### Single Responsibility Principle (SRP)
- **Each class/method must have ONE reason to change.**
- Split multi-concern classes into focused, single-purpose types.
- Services: one job (e.g., `EmailService` sends emails; `UserRepository` handles user persistence).
- Example: Don't mix logging, validation, and business logic in one method.

### Open/Closed Principle (OCP)
- **Code must be open for extension, closed for modification.**
- Use abstract base classes, interfaces, and virtual members for extension.
- New requirements = new implementations, not altered existing code.
- Example: Strategy pattern for pluggable behaviors; factory patterns for object creation variants.

### Liskov Substitution Principle (LSP)
- **Derived types must be substitutable for base types without breaking contracts.**
- Override methods must honor parent semantics; never weaken preconditions or strengthen postconditions.
- Use covariance/contravariance correctly in generics and delegates.
- Example: `IDataStore` implementer must handle all operations the interface promises.

### Interface Segregation Principle (ISP)
- **Clients should depend on minimal, specific interfaces, not fat general ones.**
- Split large interfaces into role-based, smaller interfaces (e.g., `IReadable`, `IWritable`, `IDeletable`).
- Avoid forcing implementations to implement unneeded methods.
- Example: `IRepository<T>` + `IRepository<T>.Query` instead of one "god interface."

### Dependency Inversion Principle (DIP)
- **High-level modules depend on abstractions, not low-level implementations.**
- Inject interfaces/abstractions, not concrete types.
- Use constructor injection, factory methods, or service locators (prefer constructor).
- Example: `public UserService(IRepository<User> repo, IEmailService emailer)` not `new Repository()`.

---

## C# 14 & .NET 10 Feature Usage

### Prioritized for Clean Code

1. **Extension Members** (C# 14)
   - Add cross-cutting concerns (validation, auditing) to existing types without modification.
   - Use for OCP-compliant behavior injection.
   - Example: `extension UserValidation on User { public bool IsValid() => ... }`

2. **Field-Backed Properties** (C# 14)
   - Replace verbose private fields + property pairs with `field` keyword.
   - Enable validation/logic in getters/setters without boilerplate.
   - Example: `public int Age { get => field; set => field = value > 0 ? value : throw ...; }`

3. **Primary Constructors** (C# 12+, matured in C# 14)
   - Reduce boilerplate for dependency injection.
   - All parameters automatically assigned to fields of same name (if declared).
   - Example: `public class UserService(IRepository<User> repo, ILogger logger) { ... }`

4. **Records & Immutable Types** (C# 9+, enhanced in C# 14)
   - Use for DTOs, value objects, and command/query patterns.
   - Immutability reduces side effects and reasoning complexity.
   - Example: `public record CreateUserCommand(string Email, string Name);`

5. **Nullable Reference Types** (C# 8+, strict in C# 14)
   - Enable nullability checks at compile time.
   - Make implicit nulls explicit; fail fast.
   - Example: `public class User { public string Name { get; set; } = ""; // never null }`

6. **Target-Typed New** (C# 9+)
   - Reduce redundant type names.
   - Example: `IRepository<User> repo = new PostgresUserRepository();` instead of `new PostgresUserRepository<User>(...)`

7. **Collection Initializers & Expressions** (C# 14)
   - Use collection expressions for cleaner declarations.
   - Example: `var users = [user1, user2, user3];` instead of `new List<User> { ... }`

8. **Pattern Matching** (C# 7+, enhanced)
   - Replace complex conditionals with readable patterns.
   - Example: `if (user is { Status: Active, Role: Admin }) { ... }`

9. **Async/Await Best Practices** (C# 5+, mature in .NET 10)
   - Use `async/await` for all I/O-bound operations.
   - Avoid blocking calls (`Task.Wait()`, `.Result`); use `await`.
   - Example: `public async Task<User> GetUserAsync(int id) => await repo.FindAsync(id);`

10. **Structured Concurrency & Channels** (.NET 10)
    - Use `Channel<T>` for thread-safe producer-consumer patterns.
    - Prefer `TaskScheduler` and `Task.Run()` discipline over bare threads.

---

## Architecture Layers (DDD/Clean Architecture)

Organize projects into distinct layers respecting DIP:

```
Domain/            → Core business logic, value objects, entities, interfaces
Application/       → Use cases, DTOs, orchestration (depends on Domain)
Infrastructure/    → Implementations (DB, HTTP, logging), external services (depends on Application, Domain)
Presentation/      → API/UI controllers, minimal logic (depends on Application)
Tests/             → Unit, integration, fixture builders
```

- **Domain** never depends on Infrastructure or Application.
- **Application** never depends on Infrastructure or Presentation.
- **Infrastructure/Presentation** depend on Application/Domain (outer layers depend inward).

---

## Dependency Injection & Service Registration

**Always use constructor injection:**
```csharp
public class UserService(IRepository<User> repo, IEmailService emailer)
{
    public async Task CreateUserAsync(CreateUserCommand cmd)
    {
        var user = new User(cmd.Email, cmd.Name);
        await repo.AddAsync(user);
        await emailer.SendWelcomeAsync(user.Email);
    }
}
```

**Register in composition root (Program.cs or Startup):**
```csharp
services.AddScoped<IRepository<User>, PostgresUserRepository>();
services.AddScoped<IEmailService, SmtpEmailService>();
services.AddScoped<UserService>();
```

**Never use `ServiceLocator` pattern or static `ServiceProvider` calls.**

---

## Testing Standards

### Test Structure
- **Arrange-Act-Assert (AAA) pattern** for clarity.
- **One assertion per test** (or logically grouped assertions testing one behavior).
- **Descriptive test names**: `Should_ThrowArgumentNullException_When_RepositoryIsNull()`

### Mocking & Isolation
- Mock external dependencies (DB, APIs, file system).
- Use frameworks: `Moq`, `NSubstitute`, or built-in `Mock<T>`.
- Never test implementation details; test behavior contracts.

### Test Types
- **Unit tests**: Single class in isolation, mocked dependencies.
- **Integration tests**: Multiple components, real DB (in-memory or test container).
- **E2E tests**: Full request→response cycle, external services mocked or stubbed.

**Example:**
```csharp
[Fact]
public async Task CreateUser_Should_SendWelcomeEmail_When_UserIsCreated()
{
    // Arrange
    var mockRepo = new Mock<IRepository<User>>();
    var mockEmailer = new Mock<IEmailService>();
    var service = new UserService(mockRepo.Object, mockEmailer.Object);
    var cmd = new CreateUserCommand("test@example.com", "John");

    // Act
    await service.CreateUserAsync(cmd);

    // Assert
    mockEmailer.Verify(e => e.SendWelcomeAsync("test@example.com"), Times.Once);
}
```

---

## Naming Conventions

- **Classes/Types**: PascalCase (`UserService`, `CreateUserCommand`)
- **Methods**: PascalCase, imperative verb-first (`GetUser`, `CreateUser`, `ValidateEmail`)
- **Parameters/Locals**: camelCase (`userId`, `emailAddress`)
- **Constants**: UPPER_SNAKE_CASE or PascalCase (`MAX_RETRY_COUNT`, `DefaultTimeout`)
- **Private fields**: `_camelCase` or use `field` keyword with property.
- **Interfaces**: PrefixI + PascalCase (`IRepository<T>`, `IEmailService`)
- **Async methods**: PascalCase + `Async` suffix (`GetUserAsync`, `SendEmailAsync`)
- **Booleans**: Prefix `Is`, `Has`, `Can`, `Should` (`IsActive`, `HasPermission`, `CanDelete`)

---

## Error Handling & Validation

### Fail Fast
- Validate inputs at entry points (method start).
- Throw meaningful exceptions early; don't propagate null or invalid state.
- Example:
```csharp
public UserService(IRepository<User> repo, IEmailService emailer)
{
    ArgumentNullException.ThrowIfNull(repo);
    ArgumentNullException.ThrowIfNull(emailer);
    this.repo = repo;
    this.emailer = emailer;
}
```

### Custom Exceptions
- Create domain-specific exceptions inheriting from `ApplicationException` or `InvalidOperationException`.
- Example: `public class UserNotFoundException : ApplicationException { }`

### Try-Catch Discipline
- Catch specific exceptions, not generic `Exception`.
- Log and transform exceptions at service boundaries.
- Example:
```csharp
try {
    await repo.AddAsync(user);
} catch (DbUpdateException ex) {
    logger.LogError(ex, "Failed to create user");
    throw new UserCreationFailedException("Database error", ex);
}
```

---

## Logging & Observability

- Use structured logging: `ILogger<T>` from Microsoft.Extensions.Logging.
- Log at appropriate levels: `Information` (important state changes), `Warning` (recoverable issues), `Error` (failures), `Debug` (dev investigation).
- Include correlation IDs for tracing across services.
- Example:
```csharp
logger.LogInformation("Creating user with email {Email}", user.Email);
logger.LogError("Failed to send welcome email: {Message}", ex.Message);
```

---

## Code Review Checklist

Before suggesting code, verify:

- ✅ **SRP**: Does each class have one reason to change?
- ✅ **DIP**: Are dependencies injected, not constructed?
- ✅ **ISP**: Are interfaces minimal and focused?
- ✅ **OCP**: Can new requirements be added without modifying existing code?
- ✅ **LSP**: Do derived types honor base contracts?
- ✅ **Testability**: Can this code be unit tested in isolation?
- ✅ **Nullability**: Are all reference types marked `?` if nullable or guaranteed non-null?
- ✅ **Async**: Are I/O operations async and properly awaited?
- ✅ **Naming**: Are identifiers clear, following conventions?
- ✅ **Error Handling**: Are errors caught at the right layer, with context?
- ✅ **Documentation**: Are public APIs and non-obvious logic documented?

---

## .NET 10 Specific Considerations

### Performance & Native AOT
- When generating code for AOT contexts, avoid reflection.
- Use `[DynamicallyAccessedMembers]` attributes for serialization scenarios.
- Prefer static constructors and compile-time binding.

### Security
- Use post-quantum cryptography (`MLDsa`, `MLKem`) for long-term secrets and certificates.
- Example: `var key = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);`
- Always validate and sanitize user inputs; use parameterized queries for databases.

### Async Channels & Structured Concurrency
- Use `Channel<T>` for producer-consumer patterns instead of `ConcurrentQueue<T>`.
- Example:
```csharp
var channel = Channel.CreateUnbounded<Message>();
await channel.Writer.WriteAsync(msg);
await foreach (var item in channel.Reader.ReadAllAsync()) { ... }
```

### JSON & Serialization (System.Text.Json)
- Use `JsonSerializerOptions` with sensible defaults (e.g., `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`).
- Leverage source-generated serializers for performance and AOT compatibility.
- Example:
```csharp
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var json = JsonSerializer.Serialize(user, options);
```

---

## Anti-Patterns to Avoid

- ❌ **God Classes**: Classes doing too many things. Split by SRP.
- ❌ **Service Locator**: Static `ServiceProvider.GetService()` calls. Use constructor injection.
- ❌ **Deep Nesting**: Logic nested > 3 levels. Extract methods, use early returns.
- ❌ **Synchronous Wrappers**: `Task.Wait()`, `.Result`. Use `async/await` end-to-end.
- ❌ **Fat Interfaces**: Interfaces with 10+ methods. Segregate by role.
- ❌ **Silent Failures**: Catch-swallow without logging. Log or re-throw with context.
- ❌ **Mutable Globals**: Static mutable state. Use DI; prefer immutability.
- ❌ **Null Reference Returns**: Return `null` instead of empty collections or exceptions. Use `null`-coalescing or throw.
- ❌ **Magic Strings/Numbers**: Use named constants (`MAX_RETRY_COUNT` not `5`).
- ❌ **Incomplete Async**: Methods marked `async` but not `await`-ing. Remove `async` or restructure.

---

## Example: Well-Structured Feature

```csharp
// Domain/ValueObjects/Email.cs
public record Email
{
    public string Value { get; }

    public Email(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IsValidFormat(value)) throw new ArgumentException("Invalid email format.");
        Value = value;
    }

    private static bool IsValidFormat(string email) => email.Contains("@");
}

// Domain/Entities/User.cs
public class User
{
    public int Id { get; private set; }
    public Email Email { get; private set; }
    public string Name { get; private set; }

    public User(Email email, string name)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Email = email;
        Name = name;
    }
}

// Domain/Repositories/IUserRepository.cs
public interface IUserRepository
{
    Task<User?> FindByEmailAsync(Email email);
    Task AddAsync(User user);
}

// Application/Commands/CreateUserCommand.cs
public record CreateUserCommand(string Email, string Name);

// Application/Services/CreateUserService.cs
public class CreateUserService(IUserRepository repo, IEmailService emailer, ILogger<CreateUserService> logger)
{
    public async Task ExecuteAsync(CreateUserCommand cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        logger.LogInformation("Creating user with email {Email}", cmd.Email);

        var email = new Email(cmd.Email);
        if (await repo.FindByEmailAsync(email) is not null)
            throw new InvalidOperationException("User already exists.");

        var user = new User(email, cmd.Name);
        await repo.AddAsync(user);
        await emailer.SendWelcomeAsync(user.Email.Value);

        logger.LogInformation("User created successfully: {UserId}", user.Id);
    }
}

// Tests/CreateUserServiceTests.cs
public class CreateUserServiceTests
{
    [Fact]
    public async Task Execute_Should_CreateUser_When_EmailDoesNotExist()
    {
        // Arrange
        var mockRepo = new Mock<IUserRepository>();
        var mockEmailer = new Mock<IEmailService>();
        var mockLogger = new Mock<ILogger<CreateUserService>>();
        var service = new CreateUserService(mockRepo.Object, mockEmailer.Object, mockLogger.Object);
        var cmd = new CreateUserCommand("test@example.com", "John Doe");

        // Act
        await service.ExecuteAsync(cmd);

        // Assert
        mockRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Once);
        mockEmailer.Verify(e => e.SendWelcomeAsync("test@example.com"), Times.Once);
    }

    [Fact]
    public async Task Execute_Should_ThrowInvalidOperationException_When_UserExists()
    {
        // Arrange
        var existingUser = new User(new Email("test@example.com"), "Jane");
        var mockRepo = new Mock<IUserRepository>();
        mockRepo.Setup(r => r.FindByEmailAsync(It.IsAny<Email>())).ReturnsAsync(existingUser);
        var mockEmailer = new Mock<IEmailService>();
        var mockLogger = new Mock<ILogger<CreateUserService>>();
        var service = new CreateUserService(mockRepo.Object, mockEmailer.Object, mockLogger.Object);
        var cmd = new CreateUserCommand("test@example.com", "John Doe");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(cmd));
    }
}
```

---

## Continuous Improvement

- Review generated code for alignment with these principles before committing.
- Refactor code that violates SRP or DIP.
- Update this file as team standards evolve or new C# versions introduce better patterns.
