# Arquitetura — agent-forge

Diagramas C4 (nível 1 e 2) e fronteiras do sistema.

## Nível 1 — Contexto

`agent-forge` é uma **biblioteca .NET** consumida por aplicações que precisam orquestrar agentes LLM com capabilities compostas via MCP.

```mermaid
flowchart TB
    dev(["👤 Dev que integra a lib<br/>(host CLI, serviço, aplicação .NET)"])
    forge["<b>agent-forge</b><br/><i>biblioteca .NET</i><br/>Orquestra agent loop, compõe capabilities MCP,<br/>emite traces, roda evals no CI"]
    claude["Anthropic Claude API"]
    openai["OpenAI API"]
    bedrock["AWS Bedrock"]
    mcp_docs["MCP Server<br/>Leitor de docs"]
    mcp_sql["MCP Server<br/>SQL read-only"]
    mcp_jira["MCP Server<br/>Handler Jira"]
    otel["OTel backend<br/>(local SQLite / Langfuse)"]

    dev --> forge
    forge --> claude
    forge --> openai
    forge --> bedrock
    forge --> mcp_docs
    forge --> mcp_sql
    forge --> mcp_jira
    forge --> otel

    classDef sys fill:#A57C46,stroke:#7C5B32,color:#FFF,stroke-width:2px
    classDef ext fill:#3D5675,stroke:#2B3E56,color:#FFF
    classDef person fill:#08427B,stroke:#052E5A,color:#FFF

    class forge sys
    class claude,openai,bedrock,mcp_docs,mcp_sql,mcp_jira,otel ext
    class dev person
```

**Fronteiras**

- **Dentro do escopo:** agent loop, integração com providers de modelo, cliente MCP (stdio + HTTP), instrumentação OTel, suite de evals executável no CI, guardrails leves.
- **Fora do escopo:** hosting/serverless, UI/dashboard próprio, sistema de treinamento ou fine-tuning, RAG como cerne (RAG entra como capability MCP externa, não como módulo interno).

---

## Nível 2 — Contêineres (módulos internos)

Como `agent-forge` é biblioteca, "contêineres" aqui são **módulos internos** organizados por responsabilidade. Cada um é um projeto separado na solution.

```mermaid
flowchart TB
    subgraph forge["agent-forge (.NET 9 solution)"]
        core["<b>AgentForge.Core</b><br/>Agent loop (ReAct)<br/>Session state<br/>Context assembly"]
        providers["<b>AgentForge.Providers</b><br/>Anthropic / OpenAI / Bedrock<br/>Adapters uniformes"]
        mcp["<b>AgentForge.Mcp</b><br/>Cliente MCP<br/>stdio + HTTP transport<br/>Capability discovery"]
        obs["<b>AgentForge.Observability</b><br/>OpenTelemetry instrumentation<br/>SQLite exporter local<br/>OTLP exporter opcional"]
        evals["<b>AgentForge.Evals</b><br/>LLM-as-judge<br/>Testes determinísticos<br/>Roda no xUnit / CI"]
        guards["<b>AgentForge.Guardrails</b><br/>Prompt-injection filter<br/>PII scrub<br/>Cost cap por sessão"]
    end

    core --> providers
    core --> mcp
    core --> obs
    core --> guards
    evals --> core

    classDef mod fill:#A57C46,stroke:#7C5B32,color:#FFF

    class core,providers,mcp,obs,evals,guards mod
```

**Contratos entre módulos**

- `Core` depende de abstrações (`IChatProvider`, `IMcpClient`, `ITracer`, `IGuardrail`) — nunca de implementações concretas. Isso permite swap de provider e teste unitário sem rede.
- `Providers` implementam `IChatProvider` — um por vendor (Anthropic, OpenAI, Bedrock).
- `Mcp` implementa `IMcpClient` — abstrai transport (stdio/HTTP) e serialização JSON-RPC.
- `Observability` implementa `ITracer` — thin wrapper sobre `System.Diagnostics.ActivitySource` compatível com OTel.
- `Guardrails` são plugáveis (`IGuardrail`) e rodam na ordem: PII scrub → prompt-injection filter → cost cap. Falha em qualquer um interrompe o turno.
- `Evals` é uma biblioteca de testes reutilizável — testes ficam em `tests/*.Evals` e rodam no CI como qualquer xUnit.

---

## Fluxo de um turno

Quando o Dev chama `agent.RunAsync(userMessage)`:

```mermaid
sequenceDiagram
    participant App as App do Dev
    participant Core as AgentForge.Core
    participant Guard as Guardrails
    participant Prov as Provider (Claude)
    participant Mcp as MCP Client
    participant Cap as Capability MCP

    App->>Core: RunAsync(userMessage)
    Core->>Guard: pre-check (PII, injection, cost)
    Guard-->>Core: ok
    Core->>Prov: complete(messages + tools)
    Prov-->>Core: tool_use call
    Core->>Mcp: invoke tool
    Mcp->>Cap: JSON-RPC call
    Cap-->>Mcp: result
    Mcp-->>Core: result
    Core->>Prov: complete(messages + tool_result)
    Prov-->>Core: text response
    Core->>Guard: post-check
    Guard-->>Core: ok
    Core-->>App: response + trace
```

Traces são emitidos em cada fronteira (`App→Core`, `Core→Prov`, `Core→Mcp→Cap`) via `ActivitySource`. Exportador OTel envia local (SQLite) ou remoto (Langfuse OTLP).

---

## Referências

- [ADR-001 — MCP em .NET: SDK oficial vs. custom](./adr/001-mcp-em-dotnet.md)
- [ADR-002 — Stack de evals](./adr/002-eval-stack.md)
- [ADR-003 — Tracing (a escrever)](./adr/003-tracing.md)
- Issue #1 — Design v0 (no GitHub Issues)
