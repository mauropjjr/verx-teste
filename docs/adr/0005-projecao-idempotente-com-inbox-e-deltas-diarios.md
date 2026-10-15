# 0005 — Projeção idempotente com inbox e deltas diários

- **Status:** Aceita (2026-10)
- **Decisão detalhada:** [D7](../decisoes-arquiteturais.md#d7-projeção-idempotente-e-independente-de-ordem) e [D8](../decisoes-arquiteturais.md#d8-retry-dlq-e-reprocessamento)

## Contexto

O consumidor recebe duplicatas (*at-least-once*) e eventos fora de ordem. O saldo não pode dobrar nem depender da
ordem de chegada.

## Decisão

`ProjetarLancamento`, numa transação: `INSERT ... ON CONFLICT DO NOTHING` em `mensagens_processadas` (inbox; conflito
⇒ nada muda) + UPSERT em `resumos_diarios` somando créditos e débitos do dia + `lancamentos_projetados` (saldo
intradiário) + `GREATEST` em `marcas_atualizacao`. Só somas comutativas ⇒ ordem irrelevante. Falha transitória ⇒
retry Polly (3 novas tentativas, backoff com jitter); demais ⇒ DLQ; `POST /api/v1/admin/dlq/reprocessar` devolve as
mensagens à fila.

Alternativa descartada: **saldo acumulado por dia** — leitura O(1), mas um evento atrasado reescreve todos os dias
seguintes.

## Consequências

- Efeito *exactly-once* sem coordenação distribuída; reprocessar a DLQ é seguro.
- Leitura O(dias) (soma acumulada no SQL), compensada por índice e cache; evolução: snapshots mensais.
- `mensagens_processadas` e `lancamentos_projetados` crescem sem expurgo nesta entrega.
- O cálculo foi para o SQL (*window function*); a `CalculadoraSaldo` do domínio ficou como especificação testada.
