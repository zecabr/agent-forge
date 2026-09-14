# agent-forge-file-tools

Servidor MCP standalone que expõe leitura genérica de arquivos texto sob um root local para agentes.

Segunda das três capabilities de exemplo do [agent-forge](../..) — mais generalista que o [`docs-reader`](../AgentForge.Capabilities.DocsReader): lê **qualquer arquivo texto**, não só Markdown.

## Tools expostas

| Tool | Faz o quê |
|---|---|
| `list_files` | Lista arquivos sob o root, com filtros opcionais por glob (`*.cs`) e prefixo de path (`src/`). Até 500 paths. |
| `grep` | Busca substring case-insensitive em todos os arquivos texto sob o root, com filtro opcional por glob. Até 50 matches com contexto. |
| `read_file` | Lê um arquivo texto por path relativo. Cap 200 KB, recusa binários. |

## Como rodar isolado (debug)

```powershell
dotnet run --project samples/AgentForge.Capabilities.FileTools -- --root C:\Git\Portifolio\agent-forge
```

Handshake manual pra teste rápido:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"grep","arguments":{"query":"IChatProvider","pattern":"*.cs"}}}
```

## Como plugar num agente

Adiciona no `mcp.json` do Sample CLI (junto do docs-reader, se quiser):

```json
{
  "mcpServers": {
    "file-tools": {
      "command": "dotnet",
      "args": [
        "samples/AgentForge.Capabilities.FileTools/bin/Debug/net9.0/agent-forge-file-tools.dll",
        "--root",
        "."
      ]
    }
  }
}
```

O agente vê 3 tools novas e as chama como qualquer outra.

## Segurança

- **Path traversal bloqueado.** `FilesRoot.ResolveSafely` rejeita `..`, absolutes, links que escapem.
- **Somente leitura.** Nenhuma tool escreve, cria, apaga ou move arquivos.
- **Cap de tamanho.** `read_file` recusa arquivos > 200 KB; `grep` os pula silenciosamente e reporta a contagem.
- **Detecção de binário.** Arquivos com bytes NULL nos primeiros 8 KB são pulados por `grep` e recusados por `read_file`. Heurística — não é infalível pra UTF-16, mas evita dump acidental de PNG/PDF/exe.

## Diferença pro docs-reader

| | `docs-reader` | `file-tools` |
|---|---|---|
| Escopo | só `.md` | qualquer texto |
| Descoberta | `list_docs` recursivo `.md` | `list_files` com glob |
| Busca | `search_docs` em `.md` | `grep` com filtro por glob |
| Leitura | `read_doc` até 200 KB | `read_file` até 200 KB + binary-detect |
| Uso típico | wiki / ADRs / docs | codebase / configs / textos gerais |

Os dois podem coexistir no `mcp.json` — o agente ganha 6 tools no total e escolhe qual usar por contexto.

## O que essa capability NÃO faz

- Não escreve, edita ou apaga arquivos — escopo puro de leitura.
- Não faz fuzzy matching nem embedding — busca é substring literal case-insensitive.
- Não interpreta binários (imagens, PDFs, executáveis) — `read_file` recusa; `grep` pula.
- Não segue links simbólicos que apontem pra fora do root — `ResolveSafely` bloqueia.
- Não observa mudanças no filesystem — cada chamada relê do disco.
