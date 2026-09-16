# agent-forge

[![CI](https://github.com/zecabr/agent-forge/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/zecabr/agent-forge/actions/workflows/ci.yml)
[![Evals](https://github.com/zecabr/agent-forge/actions/workflows/evals.yml/badge.svg?branch=main)](https://github.com/zecabr/agent-forge/actions/workflows/evals.yml)
[![Release](https://img.shields.io/github/v/tag/zecabr/agent-forge?label=release&sort=semver)](https://github.com/zecabr/agent-forge/releases)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/license-MIT-yellow.svg)](LICENSE)

**Orquestrador multi-agente MCP-first, opinado, em .NET.**

## Problema

Times enterprise adotando agentes de IA hoje montam orquestração de forma ad-hoc: sem eval sistemático, sem trace estruturado, sem forma clara de compor _tools_ como capabilities MCP. E o ecossistema MCP hoje é dominado por Python e TypeScript — empresas .NET ficam órfãs de framework de referência.

`agent-forge` é uma opinião concreta sobre como um agent framework MCP-first deve parecer no mundo .NET.

## O que é (v0.1.0)

- Núcleo com _agent loop_ ReAct-like, plugável entre providers de LLM. Suporte inicial: **Anthropic Claude** e **Google Gemini** (HTTP direto, sem SDKs).
- **3 capabilities MCP** entregues como servidores separados, todas cobertas por eval agentic:
  - **docs-reader** — busca e leitura em base de docs Markdown.
  - **file-tools** — listagem, grep e leitura de arquivos-fonte (ignora `.git/bin/obj/node_modules` por padrão).
  - **sql-reader** — inspeção de schema e query em SQLite read-only (`Mode=ReadOnly` + `PRAGMA query_only=1`, defesa em profundidade).
- **`McpHost` robusto** — se um server declarado no config falhar ao subir (spawn error, handshake timeout), loga no stderr e segue com os demais. Um server quebrado não derruba o host.
- **Suite de evals** — LLM-as-judge caseiro sobre xUnit ([ADR-002](docs/adr/002-eval-stack.md)), cost cap por suite, plugável por provider. Skip semântico em falhas transientes do provider — a suite só falha em resultado que não bate com o critério.
- **Retry HTTP** — `DelegatingHandler` com backoff exponencial + jitter, respeita `Retry-After` ([ADR-004](docs/adr/004-retry-http-transient.md)).
- **Guardrails**: prompt-injection filter + cost cap por sessão.
- **Sample CLI** com REPL, cost cap, cap de steps, verbose mode (`AGENT_FORGE_VERBOSE=1`) e config MCP no formato do Claude Desktop / Continue.

## O que NÃO é

- Substituto de Semantic Kernel para todo caso — é _MCP-first_, com opinião forte sobre trace e eval.
- Orquestrador visual, low-code ou UI. É biblioteca.
- Rota rápida pra colocar RAG em produção — RAG entra como capability MCP, não como cerne.

## Quick start

```bash
git clone https://github.com/zecabr/agent-forge && cd agent-forge
dotnet build
dotnet run --project tools/CreateSampleDb -- --db data/app.db   # banco de exemplo
cp mcp.example.json mcp.json
export GEMINI_API_KEY=...   # ou ANTHROPIC_API_KEY
dotnet run --project samples/AgentForge.Samples.Cli
```

REPL sobe com as 3 capabilities MCP, cost cap de $1.00 e early-brake em 90%. Aceita `--mcp <path>` pra apontar pra outra config.

## Arquitetura

Ver [`docs/architecture.md`](docs/architecture.md) e as ADRs em [`docs/adr/`](docs/adr/).

- [ADR-001 — MCP em .NET](docs/adr/001-mcp-em-dotnet.md): implementação custom sobre stdio vs. SDK oficial.
- [ADR-002 — Stack de evals](docs/adr/002-eval-stack.md): LLM-as-judge caseiro sobre xUnit.
- [ADR-003 — Tracing](docs/adr/003-tracing.md): OpenTelemetry local + exportação opcional.
- [ADR-004 — Retry HTTP transiente](docs/adr/004-retry-http-transient.md): `DelegatingHandler` sem Polly.
- [ADR-005 — Gemini thought signature](docs/adr/005-gemini-thought-signature.md): echo via `ProviderMetadata` bag.

## Roadmap curto

- **v0.1** ✅ — loop base + 3 capabilities MCP + evals + retry + guardrails.
- **v0.2+** — backlog público construído a partir de uso real. Não prometido antecipadamente.

## Stack

.NET 9 · Anthropic Messages API (HTTP direto) · Google Generative Language API (HTTP direto) · MCP JSON-RPC 2.0 sobre stdio (implementação custom, ver ADR-001) · Microsoft.Data.Sqlite · xUnit · GitHub Actions.

## Status

Released **v0.1.0** (6 previews antes de estabilizar — cada uma com um bloco de escopo isolado e uma decisão arquitetural). Contribuições bem-vindas via issues.

## Autor

Zeca — [zecabr.github.io](https://zecabr.github.io).

## Licença

MIT.
