# agent-forge-sql-reader

Servidor MCP standalone que expõe **leitura SQL read-only** contra um banco SQLite local para agentes.

Terceira das três capabilities de exemplo do [agent-forge](../..) — completa o Bloco C do plano v0.1. Ao lado do [`docs-reader`](../AgentForge.Capabilities.DocsReader) (leitura de MD) e do [`file-tools`](../AgentForge.Capabilities.FileTools) (leitura de qualquer texto), este cobre acesso estruturado a dados relacionais.

## Tools expostas

| Tool | Faz o quê |
|---|---|
| `list_tables` | Lista tabelas de usuário (exclui `sqlite_*`). |
| `describe_table` | Schema de uma tabela — colunas, tipos, nullability, defaults, PK. |
| `query` | Executa SELECT (ou `WITH ... SELECT`). Retorna TSV. Cap 100 rows, 5s timeout. |

## Segurança — defesa em profundidade

Nenhuma tool desta capability pode escrever, mesmo se o LLM mandar `INSERT`. Dois níveis:

1. **`Mode=ReadOnly` na connection string.** SQLite abre o arquivo sem lock de escrita e recusa qualquer statement que altera dados. Isso é enforcement no driver.
2. **`PRAGMA query_only = 1` após open.** Segundo cinto — desliga writes também em cenários que o driver deixaria passar (`WITH ... INSERT`, extensões de terceiros).

Adicionalmente: `command timeout = 5s` previne query lenta segurar o REPL, e `MaxRowsReturned = 100` evita dump de tabela gigante estourar o context do LLM.

**Não é sandbox anti-adversarial.** Se o usuário puder plantar um arquivo `.db` malicioso e apontar `--db` pra ele, existem exploits históricos do SQLite (CVEs raros). Escopo desta capability é *"agente lê banco que EU escolho expor"*, não *"agente executa SQL fornecido por terceiro"*.

## Como rodar isolado (debug)

```powershell
# Precisa de um .db real — crie um pequeno pra testar
sqlite3 test.db "CREATE TABLE customer(id INTEGER PRIMARY KEY, name TEXT, city TEXT); INSERT INTO customer VALUES (1, 'Zeca', 'Blumenau');"

dotnet run --project samples/AgentForge.Capabilities.SqlReader -- --db test.db
```

Handshake manual:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_tables"}}
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"query","arguments":{"sql":"SELECT name, city FROM customer LIMIT 10"}}}
```

## Como plugar num agente

Adiciona no `mcp.json` do Sample CLI:

```json
{
  "mcpServers": {
    "sql-reader": {
      "command": "dotnet",
      "args": [
        "samples/AgentForge.Capabilities.SqlReader/bin/Debug/net9.0/agent-forge-sql-reader.dll",
        "--db",
        "path/to/app.db"
      ]
    }
  }
}
```

O agente vê 3 tools novas e pode explorar o schema antes de consultar.

## O que essa capability NÃO faz

- **Não escreve.** Nem por acidente — 2 camadas de defesa.
- **Não faz DDL** (CREATE, ALTER, DROP) — bloqueado por `query_only`.
- **Não abre múltiplos DBs** — 1 processo, 1 arquivo. Rodar 2 servers pra 2 bancos.
- **Não faz join entre DBs** (ATTACH DATABASE é DDL, bloqueado).
- **Não retorna BLOBs bem-formatados** — `.ToString()` só, use hex encoding em queries se precisar.
- **Não formata para Excel/CSV** — output é TSV puro. Se precisar, o agente processa depois.

Essas escolhas são deliberadas — capability MCP de exemplo, cabe na cabeça em 15 min.
