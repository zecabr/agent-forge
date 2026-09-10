# ADR-002 — Stack de evals: caseiro leve vs. framework externo

**Status:** proposed
**Data:** 2026-09-10
**Autor:** Zeca

## Contexto

`agent-forge` precisa de uma suite de evals executável no CI — a régua entre "agente que funciona" e "agente que só parece funcionar". As opções no ecossistema:

1. **Framework externo em Python** (Ragas, DeepEval, promptfoo) — o mais maduro, mas exige rodar Python no CI e ponte de comunicação com o código .NET.
2. **Framework externo em .NET** — poucas opções maduras; `Semantic Kernel Evals` existe mas prende à arquitetura do SK.
3. **Caseiro leve sobre xUnit** — LLM-as-judge próprio + testes determinísticos, escritos como testes xUnit normais e rodando no mesmo pipeline de teste.

O ponto de tensão: framework externo maduro ganha em dashboard/relatório e em taxonomia pronta de métricas. Caseiro ganha em zero-dependência, em obrigar decisão explícita sobre o que é "sucesso" pra cada teste, e em rodar no mesmo `dotnet test` do resto.

## Decisão

**LLM-as-judge caseiro leve + testes determinísticos xUnit**, embalados no módulo `AgentForge.Evals` como biblioteca de teste reutilizável. Exportação de traces pra Langfuse via OTel (ADR-003) fornece o dashboard sem adicionar dependência de eval framework.

**Formato dos evals:**

- **Determinísticos** — asserts diretos (`Assert.Contains`, structural checks em respostas com formato conhecido). Cobrem o que dá pra verificar sem LLM (rota de tool correta, JSON válido, formato esperado).
- **LLM-as-judge** — chamada a um modelo julgador (Claude Opus por padrão) com prompt de avaliação estrito, retornando `pass|fail + razão`. Cobrem qualidade de resposta (correção factual, tom, aderência a instrução).

**Cada eval é uma classe de teste xUnit** em `tests/AgentForge.Evals.*`, roda no CI como qualquer teste. Falha um eval, quebra o build.

**Cost cap explícito.** Cada suite de eval declara um budget máximo em USD. O `AgentForge.Evals.Runner` interrompe a suite se o budget for excedido — evita CI que sangra dinheiro por bug.

## Consequências

**Positivas**

- Zero dependência externa de eval framework. `dotnet test` roda tudo.
- Força decisão explícita: pra cada eval, o autor tem que escrever o critério de sucesso — não herda uma taxonomia genérica que pode não caber.
- Julgador é plugável (mesma `IChatProvider` do runtime), então quem quiser trocar Claude por GPT ou por modelo local, faz.
- Cost cap embutido — proteção contra loop caro no CI.

**Negativas**

- Sem dashboard pronto. Analisar resultado exige ler saída do `dotnet test` ou traces no Langfuse (que é o mitigador). Não temos gráfico "eval X ao longo do tempo" no v0.1.
- Escrever LLM-as-judge caseiro é fácil de fazer errado (juiz permissivo, prompt inconsistente). Vamos precisar de um pequeno template compartilhado e review humano periódico dos julgamentos.
- Não vem com biblioteca de métricas prontas (recall, faithfulness, context precision) — se o projeto precisar disso depois, ou implementamos, ou migramos.

**Neutras**

- A trilha de migração pra framework externo (DeepEval, promptfoo) fica aberta: como a suite é xUnit puro, portar cada eval pro formato de outro framework é reescrever o assert, não redesenhar arquitetura.

## Alternativas rejeitadas

**Ragas / DeepEval em Python.** Rejeitado porque introduz Python no toolchain (`.NET dev` que roda esse projeto vira alguém que precisa de Python no CI e localmente), adiciona ponte de comunicação (HTTP ou subprocess), e a base de usuários alvo do `agent-forge` — enterprise .NET — tende a evitar essa complexidade.

**Semantic Kernel Evals.** Rejeitado porque acopla o eval à arquitetura do SK. `agent-forge` não é SK; adotar SK Evals implicaria adotar primitivas de SK que não queremos no núcleo.

**Framework externo em .NET puro.** Considerado, sem encontrar nada dominante o suficiente pra sustentar. Se a comunidade convergir num framework .NET nos próximos meses, reavaliar em v0.2.

## Revisão

Reavaliar antes do v0.2 se:

- Volume de evals passar de ~30 e dashboard virar necessidade prática.
- Ou aparecer framework .NET com adoção clara.
- Ou o custo de manter LLM-as-judge caseiro (falsos positivos frequentes) superar o custo de aprender ferramenta externa.
