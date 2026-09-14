# ADR-004 — Retry de HTTP transientes: DelegatingHandler opinado, sem Polly

**Status:** accepted
**Data:** 2026-09-14
**Autor:** Zeca

## Contexto

Os providers do agent-forge (`AnthropicChatProvider`, `GeminiChatProvider`) falam HTTP direto via `HttpClient`. Sem retry, qualquer 503/429/timeout transiente propaga como exception pro `AgentSession` e derruba o turno inteiro do agente. Ontem, no Sample CLI, o Gemini devolveu um 503 no meio de uma chamada e resolveu sozinho no minuto seguinte — mas o loop já tinha morrido.

Uma app séria precisa de retry com backoff pra códigos transientes. Precisamos decidir **onde** e **como** implementar essa política, dentro da régua "zero dependência externa no v0.1" (mesmo espírito da ADR-001).

Três caminhos considerados:

1. **Retry embutido em cada provider.** Adiciona um loop `for (attempt = 0; attempt < N; attempt++)` dentro do `CompleteAsync` de cada provider, com espera exponencial.
2. **Decorator `IChatProvider`** (`RetryingChatProvider` que envolve outro provider). Retenta em nível de provider inteiro.
3. **`DelegatingHandler` do `HttpClient`.** Encaixe idiomático .NET: um handler que fica na cadeia do `HttpClient` e retenta requisições HTTP transientes automaticamente.

Considerações que pesaram:

- **Símiles no ecossistema.** `Polly` (via `Microsoft.Extensions.Http.Polly`) é o padrão de facto. Adiciona ~4 dependências transitivas — bom pra apps grandes, peso desproporcional pra retry simples num framework enxuto.
- **Onde a política pertence.** Retry HTTP transiente é uma preocupação da **camada de transporte**, não da lógica do provider. Colocar dentro do provider mistura camadas.
- **Testabilidade.** Ambos os padrões (decorator e handler) são fáceis de testar com fakes. Handler ganha por já usar o `HttpMessageHandler` que a suíte de providers usa (`RecordingHandler`).
- **Composabilidade.** Handler compõe com outros handlers (logging, autenticação, cache) via cadeia natural. Decorator não compõe tão bem — cada nível vira uma classe.
- **Opt-in vs. default.** Um handler é opt-in: quem quer retry passa o handler; quem não quer, não passa. Isso mantém o framework opinado mas não invasivo.

## Decisão

**Implementação própria como `DelegatingHandler`, em `AgentForge.Providers/Resilience/`.**

Componentes:

- **`RetryPolicy`** — record imutável com `MaxAttempts`, `InitialDelay`, `BackoffMultiplier`, `MaxDelay`, `JitterFactor`, `RetriableStatusCodes`. Default: 3 tentativas, 100ms → 200ms → 400ms com jitter ±20%, cap em 30s. Status default: 408, 425, 429, 500, 502, 503, 504.
- **`RetryingHttpHandler : DelegatingHandler`** — encaixa sobre qualquer handler inner (tipicamente `HttpClientHandler`), retenta segundo a política. Respeita header `Retry-After` (segundos ou HTTP date), capado em `MaxDelay`. Retenta também `HttpRequestException` e `TaskCanceledException` de timeout do próprio HttpClient. Cancelamento explícito do chamador propaga sem retry.

Encaixe padrão pelo consumidor:

```csharp
var http = new HttpClient(new RetryingHttpHandler(new HttpClientHandler(), RetryPolicy.Default));
var provider = new AnthropicChatProvider(apiKey, http);
```

O Sample CLI passa a instanciar o `HttpClient` com esse handler por padrão. Quem quiser desligar (ex.: testes) passa `RetryPolicy.Disabled` ou seu próprio HttpClient sem handler.

**Zero dependência externa.** Puro `System.Net.Http` — nenhum `dotnet add package` novo.

## Consequências

**Positivas**

- Idiomático .NET — desenvolvedores que já usam `HttpClientFactory` reconhecem o padrão na mesma hora.
- Plugável em qualquer `HttpClient`, não só os do agent-forge. Se amanhã um usuário quiser aplicar em outro cliente HTTP no app dele, o handler serve.
- Testes isolados: `RetryingHttpHandlerTests` cobre a política sem tocar em provider nenhum.
- Providers ficam limpos — nenhum código de retry contamina `AnthropicChatProvider` ou `GeminiChatProvider`.

**Negativas**

- Menos features que Polly: **não** implementa circuit breaker, bulkhead, timeout policies compostas. Se um usuário precisar disso, tem que trazer Polly próprio (o handler dele compõe naturalmente na cadeia — o retry do agent-forge fica na camada de baixo).
- Fica a cargo do consumidor lembrar de instalar o handler. Um provider por acidente sem retry falha do mesmo jeito que hoje. Mitigação: o Sample CLI mostra o encaixe correto; o README v0.2 documenta.

**Neutras**

- A política default é opinionada: 3 tentativas, backoff exponencial suave. Não tenta cobrir todos os cenários; cobre os 90% comuns. Quem precisa diferente instancia `new RetryPolicy(...)`.

## Alternativas rejeitadas

**Polly / `Microsoft.Extensions.Http.Polly`.** Rejeitado por peso: retry simples não justifica ~4 dependências transitivas num framework enxuto. Se a política evoluir pra circuit breaker + bulkhead + timeout composto (v0.3+), reavaliamos.

**Retry embutido em cada provider.** Rejeitado por duplicação (mesma lógica em 2 arquivos hoje, N amanhã) e por misturar camadas. Provider fala provider; transport faz transport.

**Decorator `RetryingChatProvider`.** Rejeitado porque retenta o mapping/deserialização junto — desperdício de CPU. Handler retenta só o HTTP call, que é o que efetivamente falha.

## Revisão

Reavaliar em v0.3+ se:

- Aparecer requisito de circuit breaker (sinalizar "provider fora" após N falhas seguidas e parar de tentar por um tempo).
- Comunidade convergir num padrão diferente que baratize adoção.
- Chegarmos a >4 providers e a política uniforme começar a doer.
