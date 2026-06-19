# Contributing Guide

Thanks for your interest in **Hercules**! This document describes how to set up your environment,
format your changes, and submit them to the project.

## 📋 Contents
- [Code of Conduct](#code-of-conduct)
- [Environment Setup](#environment-setup)
- [Branch Structure](#branch-structure)
- [Commit Conventions](#commit-conventions)
- [Code Style](#code-style)
- [Pull Request Process](#pull-request-process)
- [Reporting Bugs](#reporting-bugs)

## Code of Conduct
By participating in the project, you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

## Environment Setup

### Requirements
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20+](https://nodejs.org/) (for the `hercules-web` frontend)
- (optional) [Ollama](https://ollama.com/) for local inference

### First Run
```bash
git clone https://github.com/<owner>/hercules.git
cd hercules

# Backend
dotnet restore
dotnet build Hercules.slnx

# Frontend
cd hercules-web && npm install && cd ..
```

See more: [docs/QUICKSTART.md](docs/QUICKSTART.md).

## Branch Structure
- `main` — stable branch, always builds.
- `feature/<short-name>` — new functionality.
- `fix/<short-name>` — bug fix.
- `docs/<short-name>` — documentation changes.

Create branches from the current `main`.

## Commit Conventions
We use [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <short description>

[body — optional]
```

Types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `perf`, `build`, `ci`.

Examples:
```
feat(llm): add streaming response support for Ollama
fix(memory): properly escape Cyrillic in user_profile.md
docs(api): describe the /api/skills/{id}/improve endpoint
```

## Code Style

### C#
- Follow the rules in `.editorconfig`.
- `nullable` is enabled — avoid suppressing warnings without reason.
- Comments and user-facing messages are in Russian.
- Type/method names are `PascalCase`; local variables/parameters are `camelCase`.
- Before opening a PR, run:
  ```bash
  dotnet build Hercules.slnx -warnaserror
  dotnet format Hercules.slnx --verify-no-changes
  ```

### TypeScript / Astro
- Type API calls through `src/lib/api.ts`.
- Use TailwindCSS utility classes; avoid inline styles.

## Pull Request Process
1. Create a branch from `main`.
2. Make your changes and ensure the project builds without errors and warnings.
3. Update the documentation (`README.md` / `docs/`) when behavior changes.
4. Open the PR using the template; describe the motivation and how to verify.
5. Wait for CI to pass and review to complete.

## Reporting Bugs
Open an Issue using the appropriate template. Provide:
- .NET version (`dotnet --version`) and OS;
- active LLM provider;
- reproduction steps and expected/actual behavior;
- logs or screenshots if available.

Security vulnerabilities — see [SECURITY.md](SECURITY.md) (do not open public Issues).
