# 0001 — Microsserviços enxutos em monorepo

- **Status:** Aceita (2026-10)
- **Decisão detalhada:** [D1](../decisoes-arquiteturais.md#d1-estilo-arquitetural-microsserviços-enxutos-em-monorepo)

## Contexto

O registro de lançamentos precisa continuar disponível com o consolidado fora do ar, e o consolidado precisa
aguentar 50 req/s com no máximo 5% de perda. O prazo é curto e tudo precisa subir com um `docker compose up` na
máquina do avaliador.

## Decisão

Três serviços de domínio + um worker, cada um em seu processo e container, num único repositório: `identidade-api`,
`lancamentos-api` (escrita, fonte de verdade), `consolidado-api` (leitura) e `consolidado-worker` (projeção).
Lançamentos e Consolidado se comunicam apenas pelo evento `LancamentoRegistradoV1` via RabbitMQ. Um database por
serviço (`identidade`, `lancamentos`, `consolidado`), na mesma instância Postgres local, sem acesso cruzado.

Alternativa descartada: **monólito modular** — mais simples de operar, mas um pico ou falha na leitura degradaria o
mesmo processo que registra lançamentos.

## Consequências

- Isolamento de falha e escala independente entre escrita, leitura e projeção (comprovado por
  `RecuperacaoConsolidadoTestes`: 100 de 100 registros com o consolidado parado e convergência na volta).
- Consistência eventual entre escrita e leitura, exposta por `atualizadoAte`.
- Mais peças para operar (4 processos .NET, broker, cache); o Postgres único é ponto único de falha do ambiente local.
