# 0006 — Cache-aside versionado no Redis, com fallback ao Postgres

- **Status:** Aceita (2026-10)
- **Decisão detalhada:** [D9](../decisoes-arquiteturais.md#d9-cache-e-desempenho-do-consolidado)

## Contexto

Consolidado a 50 req/s com no máximo 5% de perda, leitura O(dias). Invalidar por padrão de chave (`SCAN`/`DEL`) é caro
e sujeito a corrida. O Redis não pode virar ponto de falha da leitura.

## Decisão

`ConsultasConsolidadoEmCache`, decorator de `IConsultasConsolidado`: chaves `consolidado:{conta}:{versao}:{de}:{ate}` e
`saldo:{conta}:{versao}:{em}`; o worker faz `INCR versao:{conta}` a cada projeção (invalidação sem `DEL`). TTL de 10 min
para dias passados e 5 s se inclui hoje; saldo sem `em` truncado ao segundo. Redis fora ⇒ circuit breaker Polly +
timeouts de 500 ms ⇒ consulta direto no Postgres, e `/health/ready` responde `503`.

## Consequências

- Medido ([nfr.md](../nfr.md)): perda ≤ 0,40% com o sistema aquecido; p95 de 85,9 ms no cenário normal e 31,7 ms com
  Redis parado; p95 instável (276,6 ms e 320,1 ms) com escrita paralela; partida a frio com p95 de 1.975 ms.
- Nesta escala o Postgres responde em poucos ms e o cache não é o que garante a latência; ele protege o banco conforme
  o histórico cresce.
- A versão por conta é grossa: qualquer lançamento invalida também os períodos passados (acerto caiu de ~71% para ~30%
  com escrita paralela).
