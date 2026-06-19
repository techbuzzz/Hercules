# Web API Reference

ASP.NET Core Minimal API. Base: `http://localhost:5000`. All responses are JSON (UTF-8, camelCase).

## Authorization
All endpoints except `/api/health` require the header:
```
X-Api-Key: <value of WebApi:ApiKey>
```
If `WebApi:ApiKey` is empty, authorization is disabled (local development only).

## Endpoints

| Method | Route | Description |
|-------|---------|----------|
| `GET`  | `/api/health` | Liveness check (no key required) |
| `POST` | `/api/chat` | Message to the agent → response + mode/confidence/skill |
| `GET`  | `/api/skills` | List skills |
| `POST` | `/api/skills` | Create a skill; `?ai=true` — generate via LLM |
| `GET`  | `/api/skills/{id}` | Skill details (metadata + prompt) |
| `PUT`  | `/api/skills/{id}` | Update a skill → new version |
| `POST` | `/api/skills/{id}/improve` | Improve a skill via LLM → new version |
| `GET`  | `/api/memory/profile` | Memory profile (Markdown) |
| `PUT`  | `/api/memory/profile` | Overwrite the memory profile |
| `POST` | `/api/memory/reset` | Reset long-term memory |
| `GET`  | `/api/reflect` | Run reflection → Markdown report |
| `GET`  | `/api/stats` | Metrics: total, skill/direct, success rate, per-day |

## Examples

### Chat
```bash
curl -X POST http://localhost:5000/api/chat \
  -H "X-Api-Key: dev-local-key" \
  -H "Content-Type: application/json" \
  -d '{"message":"what is the weather in Moscow?"}'
```
Response:
```json
{
  "answer": "...",
  "mode": "direct",
  "confidence": "high",
  "provider": "yandexgpt",
  "skill": null,
  "proposeSkillForInput": false,
  "proposeImproveSkillId": null,
  "proposeImproveSkillName": null
}
```

### Creating a skill via AI
```bash
curl -X POST "http://localhost:5000/api/skills?ai=true" \
  -H "X-Api-Key: dev-local-key" -H "Content-Type: application/json" \
  -d '{"name":"Text translation","description":"Translates text between languages"}'
```

### Improving a skill
```bash
curl -X POST http://localhost:5000/api/skills/translate/improve \
  -H "X-Api-Key: dev-local-key"
```

### Statistics
```bash
curl http://localhost:5000/api/stats -H "X-Api-Key: dev-local-key"
```

## Response Codes
| Code | Meaning |
|-----|----------|
| `200` | Success |
| `400` | Invalid request (body validation) |
| `401` | Missing or invalid `X-Api-Key` |
| `404` | Skill/resource not found |
| `500` | Internal error (see server logs) |
