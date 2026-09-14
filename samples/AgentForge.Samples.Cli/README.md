# agent-forge · sample CLI

REPL simples que conversa com **Claude** (Anthropic) ou **Gemini** (Google AI Studio),
com `PromptInjectionGuardrail` e `CostCapGuardrail` ligados, retry automático em transientes 5xx/429, e **servidores MCP opcionais** (Model Context Protocol).

## Rodar com Gemini (grátis via AI Studio)

```powershell
$env:AGENT_FORGE_PROVIDER = "gemini"
$env:GEMINI_API_KEY = "sua-chave-aistudio"
dotnet run --project samples/AgentForge.Samples.Cli
```

Free tier do AI Studio dá 60 req/min no `gemini-flash-lite-latest` — suficiente pra brincar sem cobrança.

## Rodar com Claude

```powershell
$env:AGENT_FORGE_PROVIDER = "anthropic"   # ou omite — anthropic é o default
$env:ANTHROPIC_API_KEY = "sk-ant-..."
dotnet run --project samples/AgentForge.Samples.Cli
```

Digite `exit` ou Ctrl+C pra sair. Cada turno mostra o consumo cumulativo da sessão.

## Rodar com servidores MCP (DocsReader como exemplo)

O agente ganha tools quando você conecta servidores MCP. Da raiz do repo:

```powershell
# 1. Builda o docs-reader capability (garante o dll fresh)
dotnet build samples/AgentForge.Capabilities.DocsReader -c Debug

# 2. Cria/copia um mcp.json no cwd apontando pro binário
cp mcp.example.json mcp.json    # ou edita e aponta pro seu docs root

# 3. Roda o sample — vai spawnar o docs-reader e expor 3 tools pro agente
$env:AGENT_FORGE_PROVIDER = "gemini"
$env:GEMINI_API_KEY = "..."
dotnet run --project samples/AgentForge.Samples.Cli
```

O banner passa a mostrar `mcp: 1 server(s), 3 tool(s) · mcp.json` seguido dos nomes (`list_docs`, `search_docs`, `read_doc`). Peça pro agente coisas como:

- *"Liste todos os ADRs do projeto."*
- *"Procure onde a decisão sobre retry policy está documentada."*
- *"Leia o ADR-001 e resuma em 3 bullets."*

Alternativa via arg CLI ou env var (útil pra CI):

```powershell
dotnet run --project samples/AgentForge.Samples.Cli -- --mcp path/to/mcp.json
# ou
$env:AGENT_FORGE_MCP_CONFIG = "path/to/mcp.json"
dotnet run --project samples/AgentForge.Samples.Cli
```

## Config via env vars

| Var | Default | Descrição |
|---|---|---|
| `AGENT_FORGE_PROVIDER` | `anthropic` | `anthropic` ou `gemini` |
| `ANTHROPIC_API_KEY` | — (se provider=anthropic) | Chave Anthropic |
| `ANTHROPIC_WORKSPACE_ID` | — (opcional) | Necessário se sua Anthropic key for org-level |
| `GEMINI_API_KEY` | — (se provider=gemini) | Chave Google AI Studio |
| `AGENT_FORGE_MODEL` | depende do provider | `claude-3-5-sonnet-latest` (anthropic) ou `gemini-flash-lite-latest` (gemini) |
| `AGENT_FORGE_COST_CAP_USD` | `1.00` | Teto de custo da sessão em USD |
| `AGENT_FORGE_MAX_STEPS` | `5` | Passos máximos do agent loop por turno |
| `AGENT_FORGE_MCP_CONFIG` | — (opcional) | Path pro `mcp.json`; fallback `./mcp.json` no cwd. |

## Config via CLI args

| Arg | Descrição |
|---|---|
| `--mcp <path>` | Path pro `mcp.json` — tem precedência sobre env var e cwd |

## Formato do mcp.json

Compatível com Claude Desktop e Continue. Ver `mcp.example.json` na raiz do repo:

```json
{
  "mcpServers": {
    "<nome-do-server>": {
      "command": "<executável>",
      "args": ["<arg1>", "<arg2>"],
      "env": { "CHAVE": "valor" }
    }
  }
}
```

## Sobre API keys Anthropic org-level

Se a API retornar `400` com "This API key is not scoped to a workspace", sua chave é org-level. Duas saídas:
- Criar nova API key **scoped a workspace** no console Anthropic (mais simples).
- OU manter a org-level e definir `ANTHROPIC_WORKSPACE_ID` com o ID `wrkspc_...` do workspace.

## Notas

- **Guardrails ativos:** prompt-injection filter (EN+PT) e cost cap com early-brake em 90% do teto.
- **Retry automático:** falhas HTTP transientes (408, 425, 429, 500, 502, 503, 504) reentram até 3× com backoff exponencial + jitter. Ver ADR-004.
- **MCP servers:** opt-in via `mcp.json`. Sem config = chat puro, mesmo comportamento das preview.1/2. O agente sabe usar as tools MCP como qualquer outra tool.
- **Meta do §6 do PRD** ("instale, cole a chave, resolva um ticket em ≤ 5 comandos"): cumprida com Gemini free tier e MCP opcional.
