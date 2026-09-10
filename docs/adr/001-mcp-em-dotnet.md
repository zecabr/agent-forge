# ADR-001 — MCP em .NET: SDK oficial vs. implementação custom

**Status:** proposed
**Data:** 2026-09-10
**Autor:** Zeca

## Contexto

`agent-forge` é MCP-first: capabilities são expostas como servidores MCP e o núcleo consome via cliente MCP. Precisamos decidir **como falar o protocolo MCP** em .NET.

O ecossistema MCP hoje tem três caminhos:

1. **SDK oficial `ModelContextProtocol` (C#)** — mantido pela Anthropic em parceria com a Microsoft. Cobre client + server, transports stdio e HTTP (streamable), serialização JSON-RPC, primitives (tools, resources, prompts). Integração natural com `IHostBuilder` / DI do .NET.
2. **Bibliotecas comunitárias** (ex.: `mcpdotnet` original) — surgiram antes do SDK oficial. Algumas foram descontinuadas ou consolidadas no oficial; outras seguem paralelas com foco em cenários específicos.
3. **Custom sobre `System.IO.Pipelines`** — implementar transports e JSON-RPC nós mesmos, sem dependência externa.

O ponto de tensão: o SDK oficial é jovem (breaking changes possíveis, superfície de API ainda estabilizando) mas é o padrão que hosts (Claude Desktop, Copilot, editores) leem sem atrito.

## Decisão

Adotar o **SDK oficial `ModelContextProtocol`** como cliente MCP no v0.1.

**Isolamento contra churn de API.** Todo consumo do SDK passa por uma interface interna `IMcpClient` no módulo `AgentForge.Mcp`. Se o SDK quebrar API entre versões, o impacto fica confinado ao adapter dessa interface.

**Pin de versão exata** no primeiro release (não caret `^`). Upgrades são decisão explícita, documentada em CHANGELOG.

**Custom fica reservado** pra dois casos: (a) o SDK não expor um transport que precisamos (ex.: transport corporativo sobre WebSocket com auth mTLS), (b) bug bloqueante sem workaround. Nesses casos, implementamos custom **atrás da mesma `IMcpClient`**, sem trocar de API pro `Core`.

## Consequências

**Positivas**

- Alinhamento imediato com hosts MCP mainstream (Claude Desktop, Copilot). Um servidor MCP escrito pra `agent-forge` também é consumível fora dele.
- Menos código a manter — foco fica na composição de capabilities, não em plumbing JSON-RPC.
- Suporte oficial da Anthropic + Microsoft reduz risco de projeto orfanado.

**Negativas**

- Dependência de SDK em evolução ativa; upgrade pode exigir refactor do adapter.
- API do SDK pode não cobrir edge-cases enterprise específicos (auth customizada, discovery interno) — nesses casos, custom entra por trás da interface.
- Time-to-first-turn depende de dependência externa estar publicada em NuGet e funcional no ambiente do dev.

**Neutras**

- O adapter interno adiciona uma camada de indireção — em contrapartida, garante que qualquer troca futura (custom, biblioteca comunitária) seja invisível pro `Core`.

## Alternativas rejeitadas

**Bibliotecas comunitárias como cliente principal.** Rejeitadas porque (a) o mercado convergiu no SDK oficial após a consolidação, (b) hosts mainstream falam o dialeto oficial, (c) manutenção da comunidade é imprevisível pra a base de um projeto público.

**Custom desde o v0.1.** Rejeitado porque o valor de `agent-forge` está em *como compor agentes com MCP no mundo .NET*, não em reimplementar JSON-RPC. Custom seria over-engineering pro v0.1 e desperdiçaria as horas do sprint (§12 do PRD, risco alto de sobre-engenharia).

## Revisão

Reavaliar esta decisão antes do v0.2 se:

- O SDK oficial estabilizar API (chegando a 1.0.0 sem breaking changes).
- Ou o SDK oficial for descontinuado (improvável dado o compromisso Anthropic + MS).
- Ou aparecer requisito enterprise concreto que o SDK não cobre e um custom seria mais barato que múltiplos adapters.
