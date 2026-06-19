# Configuration

All settings are defined in `appsettings.json` (core) and `Hercules.WebApi/appsettings.json` (Web API),
and can also be overridden via environment variables prefixed with `HERCULES_`.

## Overriding via Environment
Nested keys are separated by double underscore `__`:
```bash
HERCULES_Llm__Provider=ollama-local
HERCULES_Llm__YandexGpt__ApiKey=***
HERCULES_Agent__SkillCreationThreshold=5
HERCULES_Telegram__Enabled=true
```

## `Llm` Section
| Key | Description | Default |
|------|----------|--------------|
| `Provider` | Active provider: `yandexgpt` / `ollama-cloud` / `ollama-local` | `yandexgpt` |
| `Fallback` | Switch order on unavailability | `["ollama-cloud","ollama-local"]` |

### `Llm:YandexGpt`
| Key | Description |
|------|----------|
| `Endpoint` | `https://llm.api.cloud.yandex.net/v1` |
| `ApiKey` | Yandex Cloud IAM or API key |
| `FolderId` | Yandex Cloud folder ID |
| `Model` | Model name; converted to `gpt://{FolderId}/{Model}/latest` |
| `Temperature` | Generation temperature (e.g. `0.6`) |
| `MaxTokens` | Maximum response tokens (e.g. `2000`) |

### `Llm:OllamaCloud`
| Key | Description |
|------|----------|
| `Endpoint` | `https://ollama.com/v1` |
| `ApiKey` | Ollama Cloud API key |
| `Model` | e.g. `gpt-oss:120b` |

### `Llm:OllamaLocal`
| Key | Description |
|------|----------|
| `Endpoint` | `http://localhost:11434/v1` |
| `ApiKey` | Not required (empty string) |
| `Model` | e.g. `llama3.1` |

> All providers work through an OpenAI-compatible interface and the
> `Microsoft.Extensions.AI` abstraction. If the active provider fails,
> `ResilientLLMClient` switches to the next one in `Fallback`.

## `Storage` Section
| Key | Description | Default |
|------|----------|--------------|
| `DataRoot` | Runtime data root | `data` |
| `SkillsDir` | Skills directory | `Skills` |
| `MemoryDir` | Memory directory | `Memory` |
| `SqliteFile` | Sessions database file | `sessions.db` |

## `Agent` Section
| Key | Description | Default |
|------|----------|--------------|
| `SystemPrompt` | Base agent system prompt | — |
| `SkillCreationThreshold` | Request repeats before proposing a skill | `3` |
| `SkillImprovementThreshold` | `success_rate` threshold for improvement | `0.6` |
| `SkillEvaluationWindow` | Skill evaluation window | `5` |
| `ReflectionEveryNCommands` | Auto-reflection every N commands | `10` |

## `Telegram` Section
| Key | Description | Default |
|------|----------|--------------|
| `Enabled` | Whether to start the bot | `false` |
| `BotToken` | Token from @BotFather | `""` |

## `WebApi` Section (`Hercules.WebApi/appsettings.json`)
| Key | Description | Default |
|------|----------|--------------|
| `ApiKey` | Key for the `X-Api-Key` header; empty string disables authorization | `dev-local-key` |
| `AllowedCorsOrigins` | Allowed CORS origins | `["http://localhost:4321","http://localhost:3000"]` |

## Frontend (`hercules-web/.env`)
| Variable | Description |
|------------|----------|
| `PUBLIC_API_BASE` | Web API base URL (e.g. `http://localhost:5000`) |
| `PUBLIC_API_KEY` | `X-Api-Key` value for requests |

> ⚠️ Do not commit real keys. Use environment variables and keep `data/` out of the repository.
