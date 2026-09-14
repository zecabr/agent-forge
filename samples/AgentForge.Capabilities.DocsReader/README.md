# agent-forge-docs-reader

Servidor MCP standalone que expõe leitura de docs Markdown local para agentes.

Uma das três capabilities de exemplo do [agent-forge](../..) — a mais didática: 100 linhas de código de negócio + biblioteca compartilhada `AgentForge.Mcp.Server`.

## Tools expostas

| Tool | Faz o quê |
|---|---|
| `list_docs` | Lista todos os `.md` recursivamente, opcionalmente filtrando por prefixo de path |
| `search_docs` | Busca case-insensitive por termo, retorna até 30 matches com linha de contexto |
| `read_doc` | Lê um `.md` inteiro por path relativo, capado em 200 KB |

## Como rodar isolado (debug do próprio servidor)

```powershell
dotnet run --project samples/AgentForge.Capabilities.DocsReader -- --root C:\Git\Portifolio\agent-forge
```

O processo lê JSON-RPC do stdin e escreve no stdout. Logs vão pro stderr — nunca poluem o canal MCP.

Handshake manual pra teste rápido (cola no stdin do processo):

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"search_docs","arguments":{"query":"MCP"}}}
```

## Como plugar num agente

No v0.2 do agent-forge, o Sample CLI aceitará servidores MCP configurados. Preview do formato:

```json
{
  "docs-reader": {
    "command": "dotnet",
    "args": ["run", "--project", "samples/AgentForge.Capabilities.DocsReader", "--", "--root", "."]
  }
}
```

O `McpHost` do agent-forge spawna o processo, faz o handshake e as três tools ficam disponíveis pro agente chamar como qualquer outra tool.

## Segurança

- **Path traversal bloqueado.** `DocsRoot.ResolveSafely` rejeita qualquer path que resolva pra fora do root — `..`, absolutes, links.
- **Somente leitura.** Nenhuma tool escreve nada. Filesystem write não é escopo desta capability.
- **Cap de tamanho.** `read_doc` recusa arquivos > 200 KB (protege contra "cai o mundo no context").
- **Cap de matches.** `search_docs` retorna no máximo 30 matches (evita esgotar tokens do turno).

## O que essa capability NÃO faz

- Não interpreta MDX / frontmatter — trata `.md` como texto puro.
- Não faz fuzzy matching nem embedding — busca é substring literal case-insensitive.
- Não faz cache — cada `search_docs` reabre e relê os arquivos. Se o root tem centenas de MB, isso incomoda.
- Não observa mudanças no filesystem — mudanças em disco viram visíveis na próxima chamada, sem push notification.

Essas escolhas são deliberadas — capability de exemplo deve caber na cabeça em 15 minutos.
