# ADR-005 — Echo de `thoughtSignature` do Gemini via `ProviderMetadata`

**Status:** accepted
**Data:** 2026-09-14
**Autor:** Zeca

## Contexto

Modelos Gemini "com thinking" (2.5 Pro, família 3.x, e o alias `gemini-flash-lite-latest` a partir de mid-2026) emitem, junto de cada `functionCall`, um `thoughtSignature` — string opaca com o "raciocínio" do modelo até aquele ponto. A API **exige** que o cliente ecoe essa signature de volta em toda request subsequente que carrega o `functionCall` no histórico:

```
400 INVALID_ARGUMENT
Function call is missing a thought_signature in functionCall parts.
This is required for tools to work correctly, and missing thought_signature
may lead to degraded model performance.
```

Reproduzimos ao plugar o `McpHost` no Sample CLI e chamar uma tool MCP (`list_docs`) em cima do `gemini-flash-lite-latest`.

Documentação: <https://ai.google.dev/gemini-api/docs/thought-signatures>

### Rota curta que não deu

Uma primeira tentativa foi desligar thinking na request via `generationConfig.thinkingConfig.thinkingBudget: 0` — o formato antigo documentado. A API respondeu `400 INVALID_ARGUMENT` genérico. O endpoint v1beta atual (setembro/2026) usa `thinking_level: "minimal" | "low" | "medium" | "high"` — sem `"off"` explícito. Desligar completamente não é opção suportada. Ficamos com signature echo real.

## Decisão

**Implementar echo de signature usando `ProviderMetadata` — um bag opcional no `ToolUseBlock`.**

Mudanças:

- `ToolUseBlock` ganha 4º parâmetro opcional `IReadOnlyDictionary<string, string>? ProviderMetadata = null`. Backward-compatible — todo construtor de 3 args continua funcionando (default `null`).
- `GeminiMapper.MapResponseFromJson`: quando o `part` tem `thoughtSignature`, captura em `ProviderMetadata["gemini.thoughtSignature"]`.
- `GeminiMapper.MapPart` (novo helper `BuildFunctionCallPart`): quando o `ToolUseBlock` carrega `gemini.thoughtSignature` no `ProviderMetadata`, escreve `thoughtSignature` no part da request.

O contrato do `ProviderMetadata`:

- **Chaves com prefixo por provider** (`gemini.thoughtSignature`, `anthropic.cacheControl`, …). Evita colisão e deixa claro quem lê o quê.
- **Providers ignoram chaves que não conhecem.** Anthropic vê `gemini.thoughtSignature` no bag e não faz nada.
- **`Agent` propaga o `ToolUseBlock` intacto** — não interpreta, não filtra.

## Consequências

**Positivas**

- Chat com Gemini em modo thinking + function call passa a funcionar multi-turn.
- Mecanismo genérico — se amanhã a Anthropic exigir echo de algo análogo (cache control, memory ids), o mesmo `ProviderMetadata` serve com prefix diferente.
- Nenhum consumidor existente do `ToolUseBlock` quebra — todos os `new ToolUseBlock(id, name, inputJson)` do repo (8 lugares) continuam válidos.

**Negativas**

- Um pequeno vazamento do domínio provider no Core — o `ToolUseBlock` do Core sabe que existe "metadata provider-specific". Justificável: manter provider quirks fora do Core exigiria round-trip de bytes opaco entre camadas, mais complexo.
- Testes de mapper agora precisam cobrir round-trip da signature em pelo menos um caso.

**Neutras**

- Se a signature vier vazia ou ausente na resposta, `ProviderMetadata` fica `null` — comportamento antigo preservado (para modelos sem thinking).

## Alternativas rejeitadas

**Desligar thinking na request (`thinkingBudget: 0`).** Rejeitado: o formato antigo foi substituído por `thinking_level` string, sem valor "off". Enviar `thinkingConfig` no v1beta devolve `400 INVALID_ARGUMENT` — testado empiricamente.

**Trocar modelo default pra um sem thinking.** Rejeitado: modelos numerados sem `-latest` viram órfãos com deprecação (aprendizado empírico 11/09 com Gemini 2.0/3.6). O alias `-latest` que a Google mantém em setembro/2026 aponta pra modelo com thinking. Fugir dele é adiar o problema.

**Bag opaco `byte[]?` em vez de dictionary tipada.** Rejeitado: `Dictionary<string, string>` com prefixo por provider fica legível em debug/tracing sem precisar de deserialização, e cobre todos os quirks que enxergamos hoje (Gemini signatures, Anthropic cache_control ids).

**Novo tipo `GeminiToolUseBlock : ToolUseBlock`.** Rejeitado: mistura camadas — Core não deveria conhecer o subtype. Além disso, o `Agent` faria pattern matching por tipo pra copiar entre turnos, quebrando o contrato de conteúdo opaco.

## Revisão

Reavaliar em v0.2 se:

- Anthropic ou outro provider exigir mecanismo análogo — hora de formalizar convenção de prefix e talvez extrair helper compartilhado.
- Aparecer necessidade de metadata **binária** (não string) — trocar o dictionary por interface mais flexível.
- O bag ficar grande a ponto de dobrar o tamanho do histórico enviado — bota resumo em campo separado.
