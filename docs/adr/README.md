# ADRs — Architecture Decision Records

Registros curtos (Status, Contexto, Decisão, Consequências) das decisões mais significativas. O raciocínio completo —
alternativas, porquês e onde está no código — fica no documento único
[decisoes-arquiteturais.md](../decisoes-arquiteturais.md), que referencia cada ADR.

| ADR | Título | Status | Decisão no documento único |
|---|---|---|---|
| [0001](0001-microsservicos-enxutos-em-monorepo.md) | Microsserviços enxutos em monorepo (estilo arquitetural) | Aceita | D1 |
| [0002](0002-clean-architecture-por-camada-com-hosts-separados.md) | Clean Architecture organizada por camada, com hosts separados | Aceita | D2 |
| [0003](0003-repository-e-unit-of-work-so-no-lado-de-escrita.md) | Repository + Unit of Work só no lado de escrita | Aceita | D2.1 |
| [0004](0004-transactional-outbox-com-rabbitmq-client.md) | Transactional Outbox com RabbitMQ.Client | Aceita | D5, D6 |
| [0005](0005-projecao-idempotente-com-inbox-e-deltas-diarios.md) | Projeção idempotente com inbox e deltas diários | Aceita | D7, D8 |
| [0006](0006-cache-aside-versionado-no-redis.md) | Cache-aside versionado no Redis, com fallback ao Postgres | Aceita | D9 |
| [0007](0007-identidade-propria-com-jwt-rs256-e-keycloak-como-evolucao.md) | Identidade própria com JWT RS256; Keycloak como evolução | Aceita (temporária) | D10 |
| [0008](0008-sem-checagem-de-saldo-no-lancamento.md) | Sem checagem de saldo suficiente no registro | Aceita | D3 |
| [0009](0009-migracoes-e-seed-na-inicializacao.md) | Migrações e seed na inicialização dos serviços | Aceita (avaliação) | D13 |

Novo ADR: próximo número livre, `NNNN-titulo-em-kebab-case.md`, mesmo formato. Um ADR substituído não é apagado: o
status passa a "Substituída por NNNN".
