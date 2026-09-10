# agent-forge · sample CLI

REPL simples que conversa com Claude via `AnthropicChatProvider`, com `PromptInjectionGuardrail` e `CostCapGuardrail` ligados.

## Rodar

```bash
# 1. definir a API key
export ANTHROPIC_API_KEY=sk-ant-...

# 2. rodar
dotnet run --project samples/AgentForge.Samples.Cli
```

No PowerShell (Windows):

```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-..."
dotnet run --project samples/AgentForge.Samples.Cli
```

Digite `exit` ou Ctrl+C pra sair. Cada turno mostra o consumo cumulativo da sessão.

## Config via env vars

| Var | Default | Descrição |
|---|---|---|
| `ANTHROPIC_API_KEY` | — (obrigatório) | Chave da API Anthropic |
| `ANTHROPIC_WORKSPACE_ID` | — (opcional) | Necessário se sua API key for org-level (ver seção abaixo) |
| `AGENT_FORGE_MODEL` | `claude-3-5-sonnet-latest` | Model ID |
| `AGENT_FORGE_COST_CAP_USD` | `1.00` | Teto de custo da sessão em USD |
| `AGENT_FORGE_MAX_STEPS` | `5` | Passos máximos do agent loop por turno |

## Sobre API keys org-level

Se a API retornar erro `400` com a mensagem *"This API key is not scoped to a workspace"*, sua chave é **org-level** e a Anthropic exige o header `anthropic-workspace-id`. Duas opções:

- **Opção A** (recomendada): criar uma API key **scoped a um workspace** no console Anthropic (Settings → API Keys → Create Key → seleciona workspace). Substituir a env var e rodar.
- **Opção B**: manter a key org-level e definir `ANTHROPIC_WORKSPACE_ID` com o ID do workspace desejado.

## Notas

- **Sem MCP no v0.1** — o sample não pluga capabilities MCP; é chat puro. Pra plugar um servidor MCP (`AgentForge.Mcp.McpHost`), veja o `docs/architecture.md` do repo — 1 sample separado sai no v0.2.
- **Guardrails ativos:** prompt-injection filter (padrões EN+PT) e cost cap com early-brake em 90% do teto.
- **Instale/execute em ≤ 5 comandos** — meta do §6 do PRD do projeto. `dotnet restore && dotnet run --project samples/AgentForge.Samples.Cli` cumpre.
