# agent-forge

**Orquestrador multi-agente MCP-first, em .NET.**

> ⚠️ Pré-lançamento. Este README descreve a promessa; código chega em v0.1.

## Problema

Times enterprise adotando agentes de IA hoje montam orquestração de forma ad-hoc: sem eval sistemático, sem trace estruturado, sem forma clara de compor _tools_ como capabilities MCP. E o ecossistema MCP hoje é dominado por Python e TypeScript — empresas .NET ficam órfãs de framework de referência.

`agent-forge` é uma opinião concreta sobre como um agent framework MCP-first deve parecer no mundo .NET.

## O que é

- Núcleo com _agent loop_ ReAct-like, plugável entre modelos (Anthropic Claude, OpenAI, Bedrock).
- Capabilities expostas como servidores MCP — 3 exemplos entregues no v0.1: leitor de docs (Docusaurus), executor de query SQL read-only, handler de Jira.
- Observabilidade via OpenTelemetry .NET + storage local em SQLite (opcional exportar para Langfuse via OTLP).
- Suite de evals — LLM-as-judge + testes determinísticos — integrada ao CI.
- Guardrails: prompt-injection filter, PII scrub, cost cap por sessão.

## O que NÃO é

- Substituto de Semantic Kernel para todo caso — é _MCP-first_, com opinião forte sobre trace e eval.
- Orquestrador visual, low-code ou UI. É biblioteca.
- Rota rápida pra colocar RAG em produção — RAG entra como capability MCP, não como cerne.

## Arquitetura

_Diagrama C4 (nível 1 e 2) publicado com a Issue #1 Design v0._

## Quick start

Adicionado no README oficial na primeira release. Meta: ≤ 5 comandos entre `git clone` e o agente resolvendo o primeiro ticket via MCP.

## Roadmap curto

- **v0.1:** loop base + 3 capabilities MCP + evals + tracing local. Uma release, uma opinião, escopo pequeno.
- **v0.2+:** backlog público construído a partir de uso real. Não prometido antecipadamente.

## Decisões arquiteturais

Publicadas em `docs/adr/` conforme forem tomadas:

- **ADR-001** — MCP em .NET: adotar implementação comunitária vs. custom sobre stdio/HTTP.
- **ADR-002** — Stack de evals: LLM-as-judge caseiro vs. framework externo.
- **ADR-003** — Tracing: OpenTelemetry local + exportação opcional para Langfuse.

## Stack

.NET 9 · Anthropic .NET SDK · OpenAI .NET SDK · MCP em .NET (comunitário ou custom, ver ADR-001) · OpenTelemetry .NET · xUnit · GitHub Actions.

## Status

Pré-release, em desenho. Repo aberto ao público na primeira release v0.1. Contribuições bem-vindas a partir daí.

## Autor

Zeca — [zecabr.github.io](https://zecabr.github.io). Um artigo técnico associado (*"MCP em .NET — por que o mundo enterprise não pode ficar de fora"*) sai junto com a v0.1.

## Licença

MIT.
