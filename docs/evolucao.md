# Evolução

O que falta para levar a plataforma de "ambiente de avaliação" a produção num banco, em ordem aproximada de
prioridade. Cada item diz **por que** (o gatilho, quase sempre um trade-off registrado em
[decisoes-arquiteturais.md](decisoes-arquiteturais.md) ou um achado de [nfr.md](nfr.md)) e **como**, aproveitando o
que já existe no código.

| # | Evolução | Gatilho | Esforço relativo |
|---|---|---|---|
| 1 | [Pool de conexões e PgBouncer](#1-pool-de-conexões-e-pgbouncer) | "too many clients" na partida a frio (medido) | Baixo |
| 2 | [Pipeline de CI/CD com critérios de qualidade](#2-pipeline-de-cicd-com-critérios-de-qualidade) | Build, testes, carga e e2e só rodam manualmente | Médio |
| 3 | [Testes de mutação](#3-testes-de-mutação) | Cobertura não prova que os testes pegam erros nas regras de dinheiro | Baixo |
| 4 | [BDD executável](#4-bdd-executável) | Cenários Given/When/Then ligados aos testes só manualmente | Baixo |
| 5 | [Identidade: Keycloak com OIDC](#5-identidade-keycloak-com-oidc) | Sem MFA, refresh, revogação, federação (D10) | Médio |
| 6 | [Cofre de segredos e rotação da chave RSA](#6-cofre-de-segredos-e-rotação-da-chave-rsa) | Segredos em variável de ambiente; chave RSA em volume (D10, D11) | Médio |
| 7 | [Gestão de APIs com Apigee](#7-gestão-de-apis-com-apigee) | Rate limit, token e contratos tratados em cada API; sem governança de consumo | Médio |
| 8 | [Migration job separado](#8-migration-job-separado) | Migração na subida das APIs (D13) | Baixo |
| 9 | [Observabilidade: OpenTelemetry e Jaeger](#9-observabilidade-opentelemetry-e-jaeger) | Só logs com correlação; sem métricas nem traces | Médio |
| 10 | [Infraestrutura como código e modelo de nuvem](#10-infraestrutura-como-código-e-modelo-de-nuvem) | Ambiente descrito só para execução local (compose) | Médio |
| 11 | [Alta disponibilidade das APIs: Kubernetes](#11-alta-disponibilidade-das-apis-kubernetes) | Uma instância de cada API e do worker; reinício não é alta disponibilidade | Alto |
| 12 | [Alta disponibilidade de RabbitMQ e Postgres](#12-alta-disponibilidade-de-rabbitmq-e-postgres) | Uma instância de cada; Postgres compartilhado (D1, D6) | Alto |
| 13 | [Snapshots mensais do consolidado](#13-snapshots-mensais-do-consolidado) | Leitura O(dias) e cache invalidado por conta inteira (D7, D9) | Médio |
| 14 | [Expurgo e higiene de dados](#14-expurgo-e-higiene-de-dados) | Outbox, inbox e movimentos projetados crescem sem limite (D5, D7) | Baixo |
| 15 | [Endurecimento HTTP](#15-endurecimento-http) | Sem TLS nem headers de segurança (D11) | Baixo |
| 16 | [Governança arquitetural](#16-governança-arquitetural) | Decisões registradas, mas sem processo para vários times | Médio |
| 17 | [Integração com os sistemas do banco: eventos e gRPC](#17-integração-com-os-sistemas-do-banco-eventos-e-grpc) | Lançamentos entram só pela API; as origens reais são outros sistemas | Alto |
| 18 | [Extração de contextos para projetos ou repositórios próprios](#18-extração-de-contextos-para-projetos-ou-repositórios-próprios) | Cada imagem carrega todos os contextos (D2) | Médio |
| 19 | [Migração do legado: strangler fig com CDC e reconciliação](#19-migração-do-legado-strangler-fig-com-cdc-e-reconciliação) | O sistema legado ainda é a fonte | Alto |

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

## 2. Pipeline de CI/CD com critérios de qualidade

**Gatilho.** Hoje build, testes, carga e e2e rodam por comandos documentados no README, mas nada garante que rodem
a cada mudança. Em um banco, a evidência de qualidade precisa ser automática e auditável.

**Como.** Pipeline em GitLab CI ou Azure DevOps (os estágios são os mesmos em Jenkins), disparado em todo merge
request e na branch principal:

| Estágio | O que roda | Bloqueia o merge quando |
|---|---|---|
| Build | `dotnet build Banco.slnx -warnaserror`, `dotnet format --verify-no-changes`, `npm run lint` e `npm run build` no `web/` | Qualquer warning ou erro |
| Testes unitários e de arquitetura | `dotnet test tests/Banco.TestesUnitarios`, `npm run test` | Qualquer falha |
| Testes de integração | `dotnet test tests/Banco.TestesIntegracao.*`, com Docker no runner (Testcontainers) | Qualquer falha |
| Testes de mutação | Stryker.NET e StrykerJS ([item 3](#3-testes-de-mutação)) | Mutation score abaixo da meta |
| Segurança | SAST (ex.: Semgrep ou SonarQube), varredura de dependências (`dotnet list package --vulnerable`, `npm audit`), varredura das imagens (ex.: Trivy) e geração de SBOM | Vulnerabilidade crítica ou alta sem exceção registrada |
| Publicação das imagens | Build das imagens dos 4 hosts e do portal, assinatura (ex.: cosign) e envio ao registry | — |
| Ambiente efêmero | `docker compose up` (ou deploy em namespace temporário), teste de sistema, e2e `@smoke` e carga k6 curta com os mesmos thresholds de [nfr.md](nfr.md) | Falha no e2e ou threshold de carga violado |
| Promoção | Deploy em homologação e produção com aprovação manual, *blue/green* ou *canary* | — |

O teste de carga de 2 minutos e o teste de sistema ficam num estágio noturno ou de pré-release, para o pipeline de
merge request não passar de alguns minutos.

## 3. Testes de mutação

**Gatilho.** A cobertura de código mostra o que foi executado, não o que foi verificado. Um teste sem asserção
relevante passa e cobre a linha. Para regras de dinheiro (valor com 2 casas, estorno, fechamento acumulado), é
preciso saber se os testes pegariam um erro.

**Como.**

- **Stryker.NET** no backend, começando por `Banco.Dominio` e `Banco.Aplicacao`: é onde estão as regras e onde os
  testes unitários são rápidos. Infraestrutura e hosts ficam de fora no início, porque cada mutante exigiria subir
  Testcontainers.
- **StrykerJS** no portal, focado em `web/src/lib/` (validação do lançamento, período, sessão).
- Meta inicial de mutation score (ex.: 80% no domínio), medida primeiro sem bloquear e depois como critério do
  pipeline ([item 2](#2-pipeline-de-cicd-com-critérios-de-qualidade)). Os mutantes sobreviventes viram testes novos.
- Execução incremental (só nos arquivos alterados) no merge request e completa à noite.

## 4. BDD executável

**Gatilho.** Os comportamentos da solução foram especificados como cenários *Given/When/Then* durante o
desenvolvimento, e os testes de integração os seguem, mas a ligação entre o cenário escrito e o teste é manual.

**Como.** Reqnroll (sucessor do SpecFlow) com os cenários em português (`Dado`/`Quando`/`Então`) em arquivos
`.feature` por contexto, reaproveitando as fábricas de teste que já existem (`FabricaApiLancamentos`,
`FabricaApiConsolidado`, `TokensTeste`). Os cenários de negócio mais importantes entram primeiro: idempotência,
estorno, consolidado com dia sem movimento e saldo em um instante. O relatório de execução vira documentação viva,
legível por quem é do negócio.

## 5. Identidade: Keycloak com OIDC

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

## 6. Cofre de segredos e rotação da chave RSA

**Gatilho.** Senhas de Postgres/RabbitMQ e senhas demo vêm de variáveis de ambiente com defaults de desenvolvimento;
a chave privada RSA é gerada e guardada num volume.

**Como.**

- Segredos em cofre (HashiCorp Vault, Azure Key Vault ou AWS Secrets Manager), injetados por provider de
  configuração do .NET ou pelo orquestrador; credenciais de banco dinâmicas e de curta duração (Vault database engine).
- Chave de assinatura em HSM/KMS (assinatura sem exportar a chave). **Rotação** sem queda: publicar a nova chave no
  JWKS antes de usá-la (o `kid` já distingue chaves e o `JwtBearer` das APIs aceita várias), passar a assinar com a
  nova, retirar a antiga depois do tempo de vida máximo do token (30 min). Com o Keycloak, a rotação passa a ser dele.

## 7. Gestão de APIs com Apigee

**Gatilho.** Hoje cada API faz sozinha o rate limit, a validação de token e a exposição de contratos. Com mais
consumidores (outros sistemas do banco, parceiros, canais), isso precisa de governança central: quem consome o quê,
com que cota, em que versão.

**Como.** Apigee (ou equivalente, como Azure API Management) na frente das três APIs:

- **Produtos de API e chaves por consumidor:** um produto "Consulta de saldo" (consolidado) e outro "Lançamentos"
  (escrita), cada um com cota e *spike arrest* próprios. O pico de 50 req/s do consolidado vira uma política de
  quota na borda, não só um limite dentro da API.
- **Segurança na borda:** validação do JWT (ou do token do Keycloak, [item 5](#5-identidade-keycloak-com-oidc)),
  bloqueio por IP e proteção contra payloads abusivos antes de chegar à aplicação.
- **Versionamento e ciclo de vida:** `/api/v1` já está nas rotas; o gateway roteia versões, avisa sobre depreciação
  e permite migrar consumidores gradualmente.
- **Contratos e portal do desenvolvedor:** gerar o OpenAPI de cada API (hoje as APIs não o expõem; o .NET 10 gera
  nativamente com `Microsoft.AspNetCore.OpenApi`) e publicá-lo no portal do Apigee, com exemplos e ambiente de testes.
- **Analytics:** latência, erros e consumo por produto e por consumidor, complementando a observabilidade do
  [item 9](#9-observabilidade-opentelemetry-e-jaeger).

**O que fica em cada lugar.** O gateway cuida do que é transversal e de borda: cota global, autenticação do
consumidor, versionamento. A autorização por papel, a idempotência e as regras de negócio continuam nas APIs, porque
dependem do domínio. O rate limit por usuário de hoje continua como segunda linha de defesa.

## 8. Migration job separado

**Gatilho.** Cada API migra o próprio banco na subida
([D13](decisoes-arquiteturais.md#d13-migrações-e-seed-na-inicialização)): várias réplicas competem, migração lenta
segura o *ready* e a conta da aplicação precisa de permissão DDL.

**Como.** Um passo do pipeline (ou Job/initContainer do orquestrador) que roda `dotnet ef migrations bundle` (um
executável por contexto) com uma credencial de DDL, antes do deploy da aplicação; a aplicação roda com credencial só
de DML. Migrações no padrão *expand/contract* para permitir deploy sem parada. O seed de demonstração não vai para
produção (`Semeadura__HistoricoHabilitado=false` e sem usuários demo).

## 9. Observabilidade: OpenTelemetry e Jaeger

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

## 10. Infraestrutura como código e modelo de nuvem

**Gatilho.** O ambiente está descrito em `docker-compose.yml`, o que já é infraestrutura declarativa, mas serve só
para execução local.

**Como.**

- **IaC:** Terraform (ou Bicep, se a nuvem for Azure) para rede, banco, cache, broker, registry e cofre, com estado
  remoto e revisão por merge request. Helm (ou Kustomize) para os 4 hosts e o portal no Kubernetes, com os
  healthchecks atuais (`/health/live` e `/health/ready`) virando *liveness* e *readiness probes*.
- **Escolha do modelo de serviço:**

| Componente | Modelo sugerido | Por quê |
|---|---|---|
| PostgreSQL, Redis, RabbitMQ (ou equivalente) | **PaaS** gerenciado | Backup, *failover* e atualização ficam com o provedor; o time foca no domínio |
| APIs, worker e portal | Containers em Kubernetes gerenciado (**PaaS**) | Escala horizontal por réplica e, no worker, pela profundidade da fila (ex.: KEDA) |
| Identidade e observabilidade | **SaaS** ou gerenciado (Keycloak gerenciado, Grafana Cloud, Datadog) | Operar essas peças não é diferencial do banco |
| Máquinas virtuais (**IaaS**) | Só quando exigido por regulação ou integração legada | Mais custo operacional |

- Desenvolvimento, homologação e produção criados pelo mesmo código, variando só os parâmetros.

## 11. Alta disponibilidade das APIs: Kubernetes

**Gatilho.** No `docker-compose.yml`, cada API e o worker rodam em um único container com `restart: unless-stopped`.
Isso é recuperação, não alta disponibilidade: enquanto o container reinicia o serviço fica fora, e se a máquina cair
tudo cai junto.

**O que já está pronto para várias réplicas.**

- **APIs sem estado:** a sessão está no JWT; nada fica em memória que precise ser compartilhado entre instâncias.
- **Publicador do outbox:** réplicas dividem o trabalho com `FOR UPDATE SKIP LOCKED`, sem publicar em dobro
  ([D5](decisoes-arquiteturais.md#d5-transactional-outbox)).
- **Worker:** consumidores concorrentes na mesma fila funcionam porque a projeção é idempotente e independente de
  ordem ([D7](decisoes-arquiteturais.md#d7-projeção-idempotente-e-independente-de-ordem)).
- **Health checks:** `/health/live` e `/health/ready` já existem em todos os hosts; o worker tem o arquivo de batimento.

**O que precisa ser resolvido antes (pré-requisitos).**

1. **Chave RSA da Identidade:** hoje fica num volume local, então cada réplica geraria a própria chave. Precisa vir
   do cofre ([item 6](#6-cofre-de-segredos-e-rotação-da-chave-rsa)).
2. **Rate limit em memória por instância:** com 3 réplicas, o limite efetivo triplica. Levar o limite global para o
   gateway ([item 7](#7-gestão-de-apis-com-apigee)) ou para o Redis, mantendo o local como segunda linha.
3. **Migração na subida:** réplicas subindo ao mesmo tempo competem pela migração
   ([item 8](#8-migration-job-separado)).

**Como (Kubernetes gerenciado: AKS, EKS ou GKE).**

- **Réplicas e zonas:** no mínimo 2 réplicas por API (3 para Lançamentos, que é o serviço crítico), espalhadas em
  zonas de disponibilidade diferentes (`topologySpreadConstraints`), com `PodDisruptionBudget` para que manutenção
  de nós nunca derrube todas ao mesmo tempo.
- **Autoescala:** HPA nas APIs por CPU e latência; no worker, KEDA escalando pela profundidade da fila
  `consolidado.lancamento-registrado`, que é o sinal real de atraso da projeção.
- **Probes:** `/health/live` como *liveness* e `/health/ready` como *readiness*, de modo que uma réplica sem banco
  sai do balanceamento sem ser reiniciada. No Lançamentos o RabbitMQ continua fora do *ready*, como hoje.
- **Deploy sem queda:** *rolling update* com `maxUnavailable: 0`; *graceful shutdown* para a API terminar as
  requisições em andamento e o worker confirmar ou devolver a mensagem que estiver processando antes de sair.
- **Rede e segurança:** *Ingress* (ou o próprio gateway) na borda, *NetworkPolicies* permitindo só o tráfego
  esperado entre serviços, segredos vindos do cofre.
- **Manifestos versionados** com Helm, como no [item 10](#10-infraestrutura-como-código-e-modelo-de-nuvem).

**Alternativa considerada: Docker Swarm.** Reaproveitaria o `docker-compose.yml` quase como está (`deploy.replicas`,
`update_config`) e é bem mais simples de operar. Foi descartada porque o ecossistema é pequeno e não oferece, sem
peças extras, o que um banco espera: autoescala por métrica de fila, distribuição por zona, *NetworkPolicies*,
integração com cofre e service mesh, e oferta gerenciada nas grandes nuvens.

**Critério de pronto.** Rodar o teste de carga do consolidado (k6, 50 req/s) e, no meio dele, derrubar uma réplica
de cada API e do worker: a perda precisa continuar abaixo de 5% e o consolidado precisa convergir sem intervenção.

## 12. Alta disponibilidade de RabbitMQ e Postgres

**Gatilho.** Uma instância de cada no compose; os três databases dividem o mesmo Postgres.

**Como.**

- **RabbitMQ:** cluster de 3 nós; as filas já são **quorum** (replicadas por Raft), então a topologia não muda. Clientes
  com lista de endpoints; a *automatic recovery* do `RabbitMQ.Client` já está ligada.
- **Postgres:** uma instância (ou cluster) **por serviço**, que os databases separados já permitem; primário + réplica
  síncrona com failover gerenciado (Patroni, ou serviço gerenciado com Multi-AZ); backups com PITR. A `consolidado-api`
  pode ler de réplica assíncrona (já tolera atraso, D7) — a projeção e o outbox ficam no primário.
- **Redis:** réplica + Sentinel, ou serviço gerenciado; perder o Redis já é tolerado (fallback ao Postgres).
- Réplicas das APIs, do publicador do outbox e do worker: ver o [item 11](#11-alta-disponibilidade-das-apis-kubernetes).

## 13. Snapshots mensais do consolidado

**Gatilho.** O fechamento de um dia soma todos os deltas anteriores (leitura O(dias)), e a invalidação do cache por
conta inteira derruba o acerto para ~30% com escrita contínua ([nfr.md](nfr.md#análise)).

**Como.** Tabela `fechamentos_mensais(conta_id, mes, fechamento)` mantida pelo worker (ou por job noturno) para meses
fechados; a consulta soma o snapshot do mês anterior a `de` + deltas do mês corrente. Evento atrasado num mês já
fechado ajusta o snapshot daquele mês e dos seguintes (raro, e O(meses), não O(dias)). Junto, versionar o cache **por
conta e mês** em vez de só por conta: um lançamento de hoje deixa de invalidar consultas de meses passados.

## 14. Expurgo e higiene de dados

`mensagens_outbox` publicadas, `mensagens_processadas` e `lancamentos_projetados` de dias fechados crescem sem limite.
Expurgo agendado: outbox publicado há mais de N dias; inbox mais antigo que a janela máxima de reentrega (retenção
da fila + DLQ); movimentos projetados de dias anteriores (o saldo intradiário só precisa do dia corrente, os dias
fechados já estão em `resumos_diarios`). Particionar `lancamentos` por tempo quando o volume justificar.

## 15. Endurecimento HTTP

TLS em todas as conexões (incluindo Postgres, Redis e AMQP), `ExigirHttpsMetadados=true`, `COOKIE_SEGURO=true`, e os
headers de segurança previstos no design e não implementados (HSTS, `X-Content-Type-Options`, CSP no portal,
`Referrer-Policy`). WAF na borda e o gateway de APIs do [item 7](#7-gestão-de-apis-com-apigee) para rate limit global e proteção contra abuso antes das APIs.

## 16. Governança arquitetural

**Gatilho.** As decisões desta entrega estão registradas em ADRs e no modelo C4, mas, em um banco com vários times, a
arquitetura precisa de um processo para continuar coerente conforme evolui.

**Como.**

- **Visão por domínios de arquitetura** (como no TOGAF): Negócio (capacidades: registrar movimentação, informar
  posição consolidada), Dados (razão imutável, projeção derivada, classificação LGPD de cada dado), Sistemas (os
  serviços e seus contratos) e Tecnologia (plataforma, rede, segurança). O C4 já cobre sistemas e parte de
  tecnologia; faltam as visões de negócio e de dados.
- **Processo de ADR com revisão:** toda decisão estrutural nova vira um ADR em `docs/adr/`, discutido no merge
  request por um fórum de arquitetura antes de ser aceito; ADRs substituídos ficam com status "Substituído por".
- **Fitness functions:** os testes de arquitetura com NetArchTest (dependência entre camadas e isolamento entre
  contextos) são o primeiro exemplo. Somar a eles os limites de latência do k6 no pipeline, contratos de evento
  versionados e a verificação de que nenhum endpoint fica sem política de autorização.
- **Catálogo de serviços e APIs** (ex.: Backstage) com dono, documentação, contratos e ADRs de cada serviço, para
  reaproveitar artefatos em vez de recriá-los.
- **Padrões de referência reutilizáveis:** o que hoje está em `Infraestrutura/Compartilhado` (padrões de host,
  autenticação, correlação, mensageria) vira um *template* de serviço ou pacote interno, para novos serviços já
  nascerem com as mesmas práticas.

## 17. Integração com os sistemas do banco: eventos e gRPC

**Gatilho.** O banco oferece crédito e seguros por cartões co-branded. Na prática, a maior parte dos lançamentos de
uma conta viria de outros sistemas (processadora de cartões, cobrança, seguros), não de um operador no portal.

**Como.**

- **Entrada por eventos:** cada sistema de origem publica seus fatos (compra autorizada, fatura paga, prêmio de
  seguro debitado) e um adaptador por origem os registra em Lançamentos. A chave de idempotência é derivada do
  identificador da origem, o mesmo mecanismo já usado no seed e proposto para o legado
  ([item 19](#19-migração-do-legado-strangler-fig-com-cdc-e-reconciliação)). Reenvios da origem não duplicam.
- **Contratos de evento governados:** `LancamentoRegistradoV1` e os eventos das origens com *schema registry* ou
  testes de contrato (ex.: Pact), versionados como já é feito com o sufixo `V1`.
- **gRPC para chamadas síncronas internas de alto volume**, quando surgirem: por exemplo, a autorização de compra do
  cartão consultando o saldo. gRPC dá contrato tipado (Protobuf), menor latência e *streaming*. REST/JSON continua
  para o portal e para consumidores externos via gateway ([item 7](#7-gestão-de-apis-com-apigee)), onde
  interoperabilidade pesa mais que desempenho.
- **SOA e EDA juntos:** serviços com contratos explícitos para consultas e comandos, e eventos para propagar fatos
  entre domínios, como já acontece entre Lançamentos e Consolidado.

## 18. Extração de contextos para projetos ou repositórios próprios

**Gatilho.** Com a organização por camada ([D2](decisoes-arquiteturais.md#d2-clean-architecture-organizada-por-camada-com-um-host-por-serviço)),
cada imagem carrega o código e os pacotes de todos os contextos, e times diferentes mexeriam nos mesmos projetos.

**Como.** Quando houver times por serviço ou ciclos de deploy independentes: mover `*/Lancamentos/` de cada camada
para `Banco.Lancamentos.{Dominio,Aplicacao,Infraestrutura}` (e idem para os demais); o que hoje está em
`Compartilhado` vira pacote NuGet interno versionado (contrato `LancamentoRegistradoV1`, padrões de host). O trabalho é
mecânico porque o isolamento entre contextos já é garantido pelos testes de arquitetura — nenhum contexto referencia
outro. O contrato do evento passa a ser governado por *schema registry* ou testes de contrato.

## 19. Migração do legado: strangler fig com CDC e reconciliação

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
