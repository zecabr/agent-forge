# ADR-003 — Tracing: OpenTelemetry sobre ActivitySource

**Status:** proposed
**Data:** 2026-09-10
**Autor:** Zeca

## Contexto

`agent-forge` precisa emitir tracing estruturado nas fronteiras do agent loop
(início de turno, chamada ao provider, invocação MCP, guardrail check). Sem
tracing, agente em produção vira caixa-preta — impossível diagnosticar por que
uma sessão gastou US$ 1,50 ou por que respondeu diferente do esperado.

Opções no ecossistema .NET:

1. **`System.Diagnostics.ActivitySource`** — API built-in do .NET (6+),
   adotada pelo OpenTelemetry como sinal principal. Zero dependência externa
   pro framework consumir. Consumer configura o TracerProvider da OpenTelemetry
   SDK e escolhe exportador (OTLP, Jaeger, Zipkin, Console, ...).
2. **OpenTelemetry.Api explícito** — pacote NuGet oficial. Superfície de API
   maior, mas embrulha `ActivitySource` no fundo. Não traz vantagem sobre
   usar `ActivitySource` diretamente.
3. **Sistema próprio (sem OTel)** — API custom, ignora ecossistema.

## Decisão

**Wrapper thin sobre `System.Diagnostics.ActivitySource`.**

- `OpenTelemetryTracer : ITracer` cria uma `ActivitySource` nomeada
  (default: `"AgentForge"`, com versão opcional).
- `ActivitySpan : ITraceSpan` embrulha uma `Activity` — quando não há listener
  registrado, a activity subjacente é null e todas as operações viram no-op.
- **Não** inclui exportador. Cabe ao consumer configurar o TracerProvider da
  OpenTelemetry SDK na aplicação:

  ```csharp
  using var provider = Sdk.CreateTracerProviderBuilder()
      .AddSource(OpenTelemetryTracer.DefaultSourceName)
      .AddOtlpExporter(o => o.Endpoint = new Uri("http://langfuse:4318"))
      .Build();
  ```

## Consequências

**Positivas**

- Compatível com qualquer backend OTel (Langfuse, Jaeger, Datadog, Grafana
  Cloud, console local) via config do consumer.
- Sem lock-in em SDK específico — a mudança de backend é uma linha de config.
- Zero overhead quando nenhum listener está ativo (o overhead do `SetTag` em
  activity null é uma checagem de null).
- Aderente ao padrão .NET moderno — bibliotecas usam `ActivitySource`,
  aplicações configuram `TracerProvider`.

**Negativas**

- Consumer precisa configurar TracerProvider — não tem "just works"
  out-of-the-box para ver traces.
- Sem exemplo de exporter embutido no repo (o README do sample cobrirá isso
  quando o v0.2 adicionar o sample com MCP + Langfuse).

**Neutras**

- Para dev local sem infra de tracing, `OpenTelemetry.Exporter.Console` da
  Anthropic SDK cospe spans no stdout — 2 linhas de config no consumer.

## Alternativas rejeitadas

**SDK exporter embutido (OTLP/SQLite built-in no framework).**  Rejeitado porque
acopla a implementação a um backend. Se o consumer quer Datadog em vez de
Jaeger, tem que ignorar/desativar o embutido — mais fricção que valor.

**Sistema custom de tracing.**  Rejeitado porque perde compatibilidade com todo
o ecossistema de observability que os operadores .NET já rodam.

## Revisão

Reavaliar antes do v0.2 se:

- Consumers reportarem que a curva de config do TracerProvider é dolorosa
  demais — nesse caso, incluir um `AddAgentForge()` helper que faz `AddSource`
  automaticamente com pipeline padrão.
- Aparecer um novo padrão .NET para tracing que suplante `ActivitySource`
  (improvável a curto prazo).
