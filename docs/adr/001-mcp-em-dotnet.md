# ADR-001 — MCP em .NET: SDK oficial vs. implementação custom

**Status:** revised
**Data:** 2026-09-10 (revisado 2026-09-10)
**Autor:** Zeca

## Contexto

`agent-forge` é MCP-first: capabilities são expostas como servidores MCP e o núcleo consome via cliente MCP. Precisamos decidir **como falar o protocolo MCP** em .NET.

O ecossistema MCP hoje tem três caminhos:

1. **SDK oficial `ModelContextProtocol` (C#)** — mantido pela Anthropic em parceria com a Microsoft. Cobre client + server, transports stdio e HTTP (streamable), serialização JSON-RPC, primitives (tools, resources, prompts). Integração natural com `IHostBuilder` / DI do .NET.
2. **Bibliotecas comunitárias** (ex.: `mcpdotnet` original) — surgiram antes do SDK oficial. Algumas foram descontinuadas ou consolidadas no oficial; outras seguem paralelas com foco em cenários específicos.
3. **Custom sobre `System.Diagnostics.Process` + JSON-RPC 2.0 line-framed** — implementar transport stdio e serialização nós mesmos, sem dependência externa.

O ponto de tensão: o SDK oficial é o padrão que hosts (Claude Desktop, Copilot, editores) leem sem atrito, mas está em versões preview com API em evolução.

## Decisão (v0.1)

**Implementação custom stdio no v0.1**, com trilha clara de migração pro SDK oficial no v0.2.

**Justificativa pragmática:**

- O SDK oficial em C# está em versões preview (breaking changes possíveis entre releases). Escrever adapter contra uma API instável, sem loop de validação rápido, aumenta risco de retrabalho e quebra o gate "CI verde desde o primeiro commit".
- O escopo do custom é pequeno e bem delimitado: `System.Diagnostics.Process` + JSON-RPC 2.0 line-delimited sobre stdio + handshake `initialize` do MCP spec. ~200 linhas, totalmente sob nosso controle.
- A interface `IMcpClient` do Core fica idêntica — a substituição pelo SDK oficial no v0.2 é invisível pro `Agent`.

**Módulo implementado:** `AgentForge.Mcp` com `IMcpTransport` (abstração), `StdioTransport` (impl real), `McpHost` (agregador que implementa `IMcpClient`).

**Não custom pra sempre.** No v0.2, migramos pro SDK oficial se:
- Ele atingir versão estável (1.0+ ou API surface travada), OU
- Aparecer requisito de transport que o custom não cobra baratamente (HTTP streamable, SSE, WebSocket com auth mTLS), OU
- A comunidade convergir de tal forma que servidores MCP passem a assumir dialeto próprio do SDK.

## Consequências

**Positivas**

- Zero dependência externa no v0.1 — o módulo `AgentForge.Mcp` compila e testa sem nenhum `dotnet add package`.
- Total controle sobre framing, error handling e cancellation.
- Adapter é substituível sem trocar API pública.

**Negativas**

- Não seguimos automaticamente evoluções do protocolo MCP — quando spec adicionar novo tipo de mensagem, temos que implementar manualmente.
- Diverge do "aterrisagem natural" que o SDK oficial dá (integração com `IHostBuilder`, serialização, primitives padronizados).
- Se o SDK oficial expor otimizações (streaming SSE, resource subscriptions), ficamos pra trás até migrar.

**Neutras**

- Custom não é "reinventar a roda toda" — é ~200 linhas de plumbing sobre `System.Diagnostics.Process` + `JsonNode`. É código que qualquer engenheiro sênior lê e compreende em 30 minutos.

## Alternativas rejeitadas

**SDK oficial imediato.** Rejeitado no v0.1 pelo risco de churn de API — validar sem loop rápido de `dotnet add package` iterativo era arriscado. Reavaliar em v0.2 (decisão registrada acima).

**Bibliotecas comunitárias.** Rejeitadas porque (a) a comunidade convergiu no SDK oficial após a consolidação, (b) manutenção de terceiros é imprevisível pra a base de um projeto público.

## Revisão

Reavaliar antes do v0.2 pelos gatilhos acima. Concretamente, se em qualquer momento a implementação custom começar a crescer pra além de ~400 linhas (adicionando transports, retry, primitives extras), essa é a bandeira de que a decisão de custom está expirando e a migração pro SDK oficial fica mais barata que o próximo incremento.
