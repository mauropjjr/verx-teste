# 0009 — Migrações e seed na inicialização dos serviços

- **Status:** Aceita para o ambiente de avaliação; em produção, substituir por *migration job*
- **Decisão detalhada:** [D13](../decisoes-arquiteturais.md#d13-migrações-e-seed-na-inicialização)

## Contexto

O avaliador roda um único comando e espera dados prontos, inclusive ~1 ano de histórico para o teste de carga.

## Decisão

Cada serviço aplica suas migrations EF Core na subida, com retry enquanto o Postgres não aceita conexões. Seed
idempotente: 3 usuários; 5 contas; ~1 ano de lançamentos pelo caso de uso interno `RegistrarHistoricoLancamentos`
(mesmo caminho de domínio, gera outbox), com o histórico inteiro e a marca `historico-lancamentos-v1` numa única
transação. O consolidado do histórico é populado pelo fluxo de produção.

## Consequências

- Zero passos manuais; a subida exercita outbox, broker e projeção em volume.
- Em produção é arriscado (réplicas competindo, migração lenta segurando o *ready*, permissão DDL na conta da
  aplicação) — evolução: job de migração separado no pipeline.
- O consolidado fica completo 1 a 2 minutos depois de os serviços ficarem `healthy`.
