# Evolução

O que falta para levar a plataforma de "ambiente de avaliação" a produção num banco, em ordem aproximada de
prioridade. Cada item diz **por que** (o gatilho, quase sempre um trade-off registrado em
[decisoes-arquiteturais.md](decisoes-arquiteturais.md) ou um achado de [nfr.md](nfr.md)) e **como**, aproveitando o
que já existe no código.

| # | Evolução | Gatilho | Esforço relativo |
|---|---|---|---|
| 1 | [Pool de conexões e PgBouncer](#1-pool-de-conexões-e-pgbouncer) | "too many clients" na partida a frio (medido) | Baixo |
| 2 | [Keycloak / OIDC](#2-identidade-keycloak-com-oidc) | Sem MFA, refresh, revogação, federação (D10) | Médio |
| 3 | [Cofre de segredos e rotação da chave RSA](#3-cofre-de-segredos-e-rotação-da-chave-rsa) | Segredos em variável de ambiente; chave RSA em volume (D10, D11) | Médio |
| 4 | [Migration job](#4-migration-job-separado) | Migração na subida das APIs (D13) | Baixo |
| 5 | [Observabilidade: OpenTelemetry e Jaeger](#observabilidade-opentelemetry-e-jaeger) | Só logs com correlação; sem métricas nem traces | Médio |
| 6 | [Alta disponibilidade de RabbitMQ e Postgres](#6-alta-disponibilidade-de-rabbitmq-e-postgres) | Uma instância de cada; Postgres compartilhado (D1, D6) | Alto |
| 7 | [Snapshots mensais do consolidado](#7-snapshots-mensais-do-consolidado) | Leitura O(dias) e cache invalidado por conta inteira (D7, D9) | Médio |
| 8 | [Expurgo e higiene de dados](#8-expurgo-e-higiene-de-dados) | Outbox, inbox e movimentos projetados crescem sem limite (D5, D7) | Baixo |
| 9 | [Endurecimento HTTP](#9-endurecimento-http) | Sem TLS nem headers de segurança (D11) | Baixo |
| 10 | [Extração de contextos para projetos/repositórios próprios](#10-extração-de-contextos-para-projetos-ou-repositórios-próprios) | Cada imagem carrega todos os contextos (D2) | Médio |
| 11 | [Migração do legado: strangler fig com CDC e reconciliação](#11-migração-do-legado-strangler-fig-com-cdc-e-reconciliação) | O sistema legado ainda é a fonte | Alto |

## 1. Pool de conexões e PgBouncer

**Gatilho (medido).** Na partida a frio da `consolidado-api`, requisições represadas (JIT, *thread pool* pequeno,
timeout no Redis abrindo o circuito) abriram conexões novas até o Postgres recusar com
`53300: sorry, too many clients already` — 12 respostas `500`, p95 de 1.975 ms e 2,50% de perda nessa rodada
([nfr.md](nfr.md#análise)). Quatro processos .NET com o pool padrão do Npgsql somam mais conexões possíveis do que o
`max_connections` padrão de um Postgres compartilhado.

**Como.**

- Fixar `Maximum Pool Size` por serviço na connection string, com a soma de todas as réplicas abaixo de
  `max_connections` (com folga para manutenção) — mudança de configuração, sem código.
- PgBouncer em *transaction pooling* na frente do Postgres quando houver várias réplicas. Atenção: o publicador do
  outbox usa transação explícita com `FOR UPDATE SKIP LOCKED` (compatível com *transaction pooling*); evitar
  *prepared statements* de sessão ou usar a versão do PgBouncer que os suporta.
- Aquecer a API antes de ficar *ready* (uma consulta de cada tipo no startup) e elevar o mínimo do *thread pool*
  (`ThreadPool.SetMinThreads`) para absorver a rampa.
- Repetir o cenário 4 da carga para medir o efeito.

## 2. Identidade: Keycloak com OIDC

**Gatilho.** A identidade própria não tem MFA, federação com o diretório corporativo, refresh token nem revogação
antes da expiração ([D10](decisoes-arquiteturais.md#d10-identidade-jwt-próprio-hoje-keycloak-amanhã)).

**Como.** As APIs já validam tokens via discovery + JWKS, então a troca é de configuração:

1. Keycloak (em HA, com banco próprio) com realm `banco`: client `banco-portal` confidencial com **Authorization Code +
   PKCE**, audiência `banco-api`, papéis `consulta`/`operador`/`admin`, **MFA** (TOTP ou WebAuthn) obrigatório para
   `operador` e `admin`, federação com AD/LDAP, política de senha e bloqueio do próprio IdP.
2. APIs: `Autenticacao:EnderecoMetadados` → discovery do realm, `Autenticacao:Emissor` → URL do realm,
   `Autenticacao:ExigirHttpsMetadados=true`. Papéis: *protocol mapper* que emite a claim `role` plana, ou um
   `IClaimsTransformation` que copia `realm_access.roles`. As policies não mudam.
3. Portal: o login vira redirect ao Keycloak e troca do código no servidor (Route Handler), com access token curto
   e **refresh token** guardados no cookie httpOnly (ou numa sessão server-side referenciada pelo cookie); logout
   encerra a sessão no IdP (revogação). `proxy.ts` continua protegendo as rotas.
4. Migrar usuários (ou federar) e desligar a `identidade-api`.

Para chamadas serviço-a-serviço futuras: *client credentials* com audiência própria.

## 3. Cofre de segredos e rotação da chave RSA

**Gatilho.** Senhas de Postgres/RabbitMQ e senhas demo vêm de variáveis de ambiente com defaults de desenvolvimento;
a chave privada RSA é gerada e guardada num volume.

**Como.**

- Segredos em cofre (HashiCorp Vault, Azure Key Vault ou AWS Secrets Manager), injetados por provider de
  configuração do .NET ou pelo orquestrador; credenciais de banco dinâmicas e de curta duração (Vault database engine).
- Chave de assinatura em HSM/KMS (assinatura sem exportar a chave). **Rotação** sem queda: publicar a nova chave no
  JWKS antes de usá-la (o `kid` já distingue chaves e o `JwtBearer` das APIs aceita várias), passar a assinar com a
  nova, retirar a antiga depois do tempo de vida máximo do token (30 min). Com o Keycloak, a rotação passa a ser dele.

## 4. Migration job separado

**Gatilho.** Cada API migra o próprio banco na subida
([D13](decisoes-arquiteturais.md#d13-migrações-e-seed-na-inicialização)): várias réplicas competem, migração lenta
segura o *ready* e a conta da aplicação precisa de permissão DDL.

**Como.** Um passo do pipeline (ou Job/initContainer do orquestrador) que roda `dotnet ef migrations bundle` (um
executável por contexto) com uma credencial de DDL, antes do deploy da aplicação; a aplicação roda com credencial só
de DML. Migrações no padrão *expand/contract* para permitir deploy sem parada. O seed de demonstração não vai para
produção (`Semeadura__HistoricoHabilitado=false` e sem usuários demo).

## Observabilidade: OpenTelemetry e Jaeger

**Gatilho.** Hoje há logs JSON com `CorrelationId` ponta a ponta (HTTP → outbox → AMQP → projeção) e health checks,
mas nenhuma métrica nem trace ([decisões — Observabilidade](decisoes-arquiteturais.md#observabilidade)).

**Como.**

- OpenTelemetry SDK nos quatro hosts e no Next.js: instrumentação de ASP.NET Core, HttpClient, Npgsql, Redis e
  RabbitMQ; propagar `traceparent` (W3C) também no cabeçalho AMQP, ao lado de `x-correlation-id`, e gravá-lo no
  outbox para o trace atravessar a fronteira assíncrona.
- OTel Collector → Jaeger (traces), Prometheus (métricas) e Loki ou similar (logs), com Grafana.
- Métricas de negócio/operação que faltam: idade da mensagem pendente mais antiga no outbox, profundidade da fila
  e da DLQ, lag de projeção (`agora − atualizadoAte`), taxa de acerto do cache, estado do circuito do Redis, `429` por
  usuário.
- Alertas: DLQ não vazia, outbox pendente há mais de N minutos, lag de projeção acima do SLO, circuito aberto.

## 6. Alta disponibilidade de RabbitMQ e Postgres

**Gatilho.** Uma instância de cada no compose; os três databases dividem o mesmo Postgres.

**Como.**

- **RabbitMQ:** cluster de 3 nós; as filas já são **quorum** (replicadas por Raft), então a topologia não muda. Clientes
  com lista de endpoints; a *automatic recovery* do `RabbitMQ.Client` já está ligada.
- **Postgres:** uma instância (ou cluster) **por serviço**, que os databases separados já permitem; primário + réplica
  síncrona com failover gerenciado (Patroni, ou serviço gerenciado com Multi-AZ); backups com PITR. A `consolidado-api`
  pode ler de réplica assíncrona (já tolera atraso, D7) — a projeção e o outbox ficam no primário.
- **Redis:** réplica + Sentinel, ou serviço gerenciado; perder o Redis já é tolerado (fallback ao Postgres).
- Réplicas das APIs atrás de balanceador; o publicador do outbox já divide trabalho com `SKIP LOCKED` e o worker
  escala por consumidores concorrentes (a projeção é comutativa).

## 7. Snapshots mensais do consolidado

**Gatilho.** O fechamento de um dia soma todos os deltas anteriores (leitura O(dias)), e a invalidação do cache por
conta inteira derruba o acerto para ~30% com escrita contínua ([nfr.md](nfr.md#análise)).

**Como.** Tabela `fechamentos_mensais(conta_id, mes, fechamento)` mantida pelo worker (ou por job noturno) para meses
fechados; a consulta soma o snapshot do mês anterior a `de` + deltas do mês corrente. Evento atrasado num mês já
fechado ajusta o snapshot daquele mês e dos seguintes (raro, e O(meses), não O(dias)). Junto, versionar o cache **por
conta e mês** em vez de só por conta: um lançamento de hoje deixa de invalidar consultas de meses passados.

## 8. Expurgo e higiene de dados

`mensagens_outbox` publicadas, `mensagens_processadas` e `lancamentos_projetados` de dias fechados crescem sem limite.
Expurgo agendado: outbox publicado há mais de N dias; inbox mais antigo que a janela máxima de reentrega (retenção
da fila + DLQ); movimentos projetados de dias anteriores (o saldo intradiário só precisa do dia corrente, os dias
fechados já estão em `resumos_diarios`). Particionar `lancamentos` por tempo quando o volume justificar.

## 9. Endurecimento HTTP

TLS em todas as conexões (incluindo Postgres, Redis e AMQP), `ExigirHttpsMetadados=true`, `COOKIE_SEGURO=true`, e os
headers de segurança previstos no design e não implementados (HSTS, `X-Content-Type-Options`, CSP no portal,
`Referrer-Policy`). Gateway de API/WAF na borda para rate limit global e proteção contra abuso antes das APIs.

## 10. Extração de contextos para projetos ou repositórios próprios

**Gatilho.** Com a organização por camada ([D2](decisoes-arquiteturais.md#d2-clean-architecture-organizada-por-camada-com-um-host-por-serviço)),
cada imagem carrega o código e os pacotes de todos os contextos, e times diferentes mexeriam nos mesmos projetos.

**Como.** Quando houver times por serviço ou ciclos de deploy independentes: mover `*/Lancamentos/` de cada camada
para `Banco.Lancamentos.{Dominio,Aplicacao,Infraestrutura}` (e idem para os demais); o que hoje está em
`Compartilhado` vira pacote NuGet interno versionado (contrato `LancamentoRegistradoV1`, padrões de host). O trabalho é
mecânico porque o isolamento entre contextos já é garantido pelos testes de arquitetura — nenhum contexto referencia
outro. O contrato do evento passa a ser governado por *schema registry* ou testes de contrato.

## 11. Migração do legado: strangler fig com CDC e reconciliação

**Gatilho.** Em produção, o sistema legado de fluxo de caixa é a fonte atual e não pode ser desligado de uma vez.

**Como (por conta, de forma reversível).**

1. **Espelhar:** CDC (ex.: Debezium) no banco do legado publica as movimentações; um adaptador as registra em
   Lançamentos pelo mesmo caminho de domínio do seed (`RegistrarHistoricoLancamentos`), com chave de idempotência
   derivada do id do legado — reprocessar o CDC não duplica.
2. **Reconciliar diariamente:** job compara, por conta e dia, o saldo do legado com o fechamento do consolidado;
   divergência gera alerta e bloqueia o avanço daquela conta.
3. **Virar a leitura** por conta (ou grupo de contas): consultas de saldo passam a vir do consolidado; o legado ainda
   recebe escrita.
4. **Virar a escrita** por conta: os canais passam a registrar em Lançamentos, e o legado vira consumidor do evento
   `LancamentoRegistradoV1` enquanto existir.
5. **Desligar** o legado quando nenhuma conta depender dele, mantendo o histórico importado.

O desenho atual ajuda: idempotência por chave, eventos versionados e sem dados pessoais, projeção independente de
ordem (eventos do CDC podem chegar atrasados) e `atualizadoAte` para expor o atraso durante a transição.
