---
id: code-execution
name: Code Execution
version: 1
phrase_receivers:
  - "напиши код"
  - "запусти код"
  - "выполни код"
  - "посчитай"
  - "вычисли"
  - "сгенерируй скрипт"
  - "execute"
  - "run code"
  - "compute"
description: |
  Запуск C# кода в sandbox через `dotnet run --file` (file-based apps, .NET 10).
  Агент генерирует C# код, DangerousCodeScanner проверяет его, executor запускает
  в изолированной temp-директории с timeout + ulimit.

  Безопасно по умолчанию:
  - Сеть запрещена (HttpClient/Socket блокируются)
  - Process.Start блокируется
  - File.Delete/Directory.Delete блокируются
  - Fork-bomb защита (ulimit -u)
  - 30s wall-clock timeout

  Escape hatch — CustomAllowedNamespaces для легитимных случаев (например, HttpClient
  для fetch-скилла).
---

# Code Execution Skill

Когда пользователь просит **написать код, посчитать что-то, выполнить скрипт** — генерируешь
C# код для file-based apps и передаёшь в `dotnet run --file` через sandbox.

## Когда вызывать

- «Посчитай факториал 100»
- «Сгенерируй CSV с первыми 100 простыми числами»
- «Напиши скрипт, который делает X» (когда X — это вычисление или обработка данных)
- «Запусти этот код»

## Когда НЕ вызывать

- Нужна сеть (HTTP-запросы) → используй HTTP skill (отдельный, Stage 3)
- Нужно писать в произвольные файлы хоста → out of scope
- Нужно запустить чужой непроверенный код → откажись

## Как генерировать код

1. Пиши **полный файл** (top-level statements, не вызов функции)
2. Используй только `System.*` namespaces по умолчанию
3. Для output — `Console.WriteLine`
4. Для данных — in-memory коллекции или файлы в `%TEMP%`

### Пример: факториал

```csharp
int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);
Console.WriteLine(Factorial(20));
```

### Пример: простые числа

```csharp
int count = 0;
for (int n = 2; n <= 1000; n++)
{
    bool isPrime = true;
    for (int d = 2; d * d <= n; d++)
        if (n % d == 0) { isPrime = false; break; }
    if (isPrime) count++;
}
Console.WriteLine($"Primes up to 1000: {count}");
```

## Что вернуть пользователю

После выполнения сообщи:
- ✅/❌ статус (ok / failed / timeout / rejected)
- stdout (если есть)
- Краткий комментарий что произошло

## Ограничения sandbox

- **30 секунд** на выполнение
- **Без сети** (HttpClient заблокирован)
- **Без Process.Start**
- **Без File.Delete/Directory.Delete** (если нужно очистить — попроси пользователя)
- **Без реестра, без DllImport, без Assembly.LoadFile**
- Максимум 100 КБ кода
- Рабочая директория = изолированная temp-папка (создаётся и удаляется автоматически)

## Pitfalls

- ❌ Не используй `File.Delete` — sandbox заблокирует
- ❌ Не пиши `while(true)` без причины — убьёт по timeout
- ❌ Не используй `new HttpClient()` — нет сети
- ❌ Не пытайся вызвать `Process.Start("rm", "-rf")` — заблокировано
- ✅ Пиши чистый вычислительный код с `Console.WriteLine` для output
- ✅ Для больших данных — пиши в `%TEMP%/hercules-output.csv`, потом сообщи пользователю путь
