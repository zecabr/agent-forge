# agent-forge · sample CLI

REPL simples que conversa com **Claude** (Anthropic) ou **Gemini** (Google AI Studio),
com `PromptInjectionGuardrail` e `CostCapGuardrail` ligados.

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

## Sobre API keys Anthropic org-level

Se a API retornar `400` com "This API key is not scoped to a workspace", sua chave é org-level. Duas saídas:
- Criar nova API key **scoped a workspace** no console Anthropic (mais simples).
- OU manter a org-level e definir `ANTHROPIC_WORKSPACE_ID` com o ID `wrkspc_...` do workspace.

## Notas

- **Sem MCP no sample v0.1** — chat puro. Plugar `AgentForge.Mcp.McpHost` no sample é sub-issue do v0.2.
- **Guardrails ativos:** prompt-injection filter (EN+PT) e cost cap com early-brake em 90% do teto.
- **Meta do §6 do PRD** ("instale, cole a chave, resolva um ticket em ≤ 5 comandos"): cumprida com `dotnet restore && dotnet run --project samples/AgentForge.Samples.Cli`.
