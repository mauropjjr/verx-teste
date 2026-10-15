# C4 — Nível 3: Componentes de Lançamentos

Dentro do container `lancamentos-api`. O host `Banco.Api.Lancamentos` compõe só o contexto Lançamentos
(`AdicionarInfraestruturaLancamentos`, `AdicionarPublicadorOutboxLancamentos`, `AdicionarValidacaoLancamentos`).
É o **lado de escrita** e a fonte de verdade; não conhece o Consolidado — só publica o contrato
`LancamentoRegistradoV1` (`Banco.Aplicacao/Compartilhado/Contratos`).

```mermaid
C4Component
  title Componentes - lancamentos-api

  Container(web, "Portal web", "Next.js", "Server Actions e páginas")
  ContainerDb(pglanc, "Database lancamentos", "PostgreSQL 17", "contas, lancamentos, mensagens_outbox, semeaduras")
  ContainerQueue(rabbit, "RabbitMQ", "RabbitMQ 4", "Exchange lancamentos.eventos")

  Container_Boundary(host, "lancamentos-api (Banco.Api.Lancamentos)") {
    Component(endpoints, "EndpointsContas + EndpointsLancamentos", "Minimal API + FluentValidation", "Rotas /api/v1/contas, policies PodeConsultar e PodeOperar")
    Component(casos, "RegistrarLancamento / EstornarLancamento", "Aplicacao, casos de uso", "Idempotência por Idempotency-Key e evento na caixa de saída")
    Component(leitura, "ListarContas / ConsultarExtrato", "Aplicacao, casos de uso", "Leitura de contas e extrato paginado por cursor")
    Component(dominio, "Conta + Lancamento", "Dominio, agregados", "Valor positivo, imutável, estorno compensatório")
    Component(repos, "RepositorioContas + RepositorioLancamentos", "Infraestrutura, EF Core", "Portas IRepositorioContas e IRepositorioLancamentos")
    Component(caixa, "CaixaSaidaOutbox", "Infraestrutura", "Porta ICaixaSaidaEventos: adiciona MensagemOutbox com id = id do lançamento")
    Component(uow, "UnidadeDeTrabalhoLancamentos", "Infraestrutura, EF Core", "SaveChanges único; UNIQUE violada vira ResultadoConfirmacao")
    Component(extrato, "ConsultasExtrato", "Infraestrutura, SQL", "Porta IConsultasExtrato, sem repositório")
    Component(publicador, "PublicadorOutbox", "BackgroundService, RabbitMQ.Client", "Lotes FOR UPDATE SKIP LOCKED, publisher confirms, backoff")
    Component(seed, "InicializadorBancoLancamentos + SemeadorLancamentos", "Infraestrutura", "Migração, 5 contas e 1 ano de histórico via RegistrarHistoricoLancamentos")
  }

  Rel(web, endpoints, "REST + Bearer JWT", "HTTP/JSON")
  Rel(endpoints, casos, "ExecutarAsync")
  Rel(endpoints, leitura, "ExecutarAsync")
  Rel(casos, dominio, "Registrar / Estornar")
  Rel(casos, repos, "Obtém e adiciona")
  Rel(casos, caixa, "Adicionar evento")
  Rel(casos, uow, "ConfirmarAsync")
  Rel(leitura, extrato, "ListarAsync")
  Rel(uow, pglanc, "Lançamento + outbox, 1 transação", "Npgsql")
  Rel(extrato, pglanc, "SELECT por cursor", "Npgsql")
  Rel(publicador, pglanc, "Lê pendentes e marca publicado_em", "Npgsql")
  Rel(publicador, rabbit, "lancamento.registrado.v1", "AMQP")
  Rel(seed, pglanc, "Migra e semeia", "Npgsql")

  UpdateLayoutConfig($c4ShapeInRow="3", $c4BoundaryInRow="1")
```

**Legenda:** como no [nível 3 da Identidade](03-componentes-identidade.md): componentes dentro da fronteira,
containers vizinhos fora, camada indicada na tecnologia.

## Mesma visão em flowchart

Mesmos componentes do diagrama C4 acima, agrupados por camada; o rótulo das setas é a **porta** da Aplicacao que a
Infraestrutura implementa.

```mermaid
flowchart LR
  web["Portal web"]:::ext
  subgraph host["Banco.Api.Lancamentos (host)"]
    endpoints["EndpointsContas +<br/>EndpointsLancamentos"]:::host
  end
  subgraph app["Banco.Aplicacao/Lancamentos"]
    escrita["RegistrarLancamento<br/>EstornarLancamento"]:::app
    historico["RegistrarHistoricoLancamentos<br/><small>(só o seed)</small>"]:::app
    leitura["ListarContas<br/>ConsultarExtrato"]:::app
  end
  subgraph dom["Banco.Dominio/Lancamentos"]
    agregados["Conta · Lancamento"]:::dom
  end
  subgraph infra["Banco.Infraestrutura/Lancamentos"]
    repos["RepositorioContas<br/>RepositorioLancamentos"]:::infra
    caixa["CaixaSaidaOutbox"]:::infra
    uow["UnidadeDeTrabalhoLancamentos"]:::infra
    extrato["ConsultasExtrato"]:::infra
    publicador["PublicadorOutbox<br/><small>BackgroundService</small>"]:::infra
    seed["SemeadorLancamentos"]:::infra
  end
  pglanc[("database lancamentos")]:::banco
  rabbit[["RabbitMQ<br/>lancamentos.eventos"]]:::banco

  web --> endpoints
  endpoints --> escrita
  endpoints --> leitura
  escrita --> agregados
  escrita -- "IRepositorio*" --> repos
  escrita -- "ICaixaSaidaEventos" --> caixa
  escrita -- "IUnidadeDeTrabalhoLancamentos" --> uow
  leitura -- "IConsultasExtrato" --> extrato
  seed --> historico
  historico --> agregados
  uow -- "lançamento + outbox<br/>1 transação" --> pglanc
  extrato --> pglanc
  publicador -- "SKIP LOCKED" --> pglanc
  publicador -- "confirms" --> rabbit

  classDef ext fill:#999999,stroke:#6b6b6b,color:#fff
  classDef host fill:#85bbf0,stroke:#5d82a8,color:#000
  classDef app fill:#85bbf0,stroke:#5d82a8,color:#000
  classDef dom fill:#85bbf0,stroke:#5d82a8,color:#000
  classDef infra fill:#85bbf0,stroke:#5d82a8,color:#000
  classDef banco fill:#438dd5,stroke:#2e6295,color:#fff
```

## Elementos

| Elemento | Responsabilidade | Tecnologia |
|---|---|---|
| `EndpointsContas` | `GET /api/v1/contas` (titular mascarado — `Conta.TitularMascarado`) | Minimal API, `PodeConsultar` |
| `EndpointsLancamentos` | `GET /api/v1/contas/{contaId}/lancamentos` (extrato, `PodeConsultar`); `POST .../lancamentos` e `POST .../lancamentos/{id}/estorno` (`PodeOperar`, `Idempotency-Key` obrigatório). Mapeia `ResultadoLancamento` para `201`, `404`, `409` (segundo estorno) e `422` (chave reutilizada com outro corpo, estorno de estorno, conta inativa) | Minimal API + validadores FluentValidation (`ValidacaoEntrada`) |
| `RegistrarLancamento`, `EstornarLancamento` | Casos de uso de escrita (um por classe, `ExecutarAsync`). Calculam `HashRequisicao` (SHA-256 do corpo normalizado), consultam reenvio (`Idempotencia.ResultadoAnteriorAsync`), criam o agregado, adicionam o evento e confirmam. Perderam a corrida na UNIQUE ⇒ respondem com o lançamento vencedor | `Banco.Aplicacao/Lancamentos` |
| `RegistrarHistoricoLancamentos` | Caso de uso **interno** do seed: mesmo caminho de domínio, instante vindo do item e confirmação em lotes de 1.000. Não é exposto por rota | `Banco.Aplicacao/Lancamentos` |
| `ListarContas`, `ConsultarExtrato` | Leitura; o extrato usa `CursorExtrato` (`ocorrido_em`, `id`) | `Banco.Aplicacao/Lancamentos` |
| `Conta`, `Lancamento` | Agregados: valor `decimal` positivo, tipo `Credito`/`Debito`, `Lancamento.Estornar` gera o compensatório de tipo oposto; um estorno não pode ser estornado | `Banco.Dominio/Lancamentos` |
| `RepositorioContas`, `RepositorioLancamentos` | Um repositório por agregado (sem CRUD genérico); lançamentos só são adicionados | EF Core |
| `CaixaSaidaOutbox` | `ICaixaSaidaEventos.Adicionar` cria `MensagemOutbox` (`Id` = id do lançamento, `Tipo = lancamento.registrado.v1`, `Conteudo` JSON, `IdCorrelacao` da requisição) no mesmo `DbContext` | EF Core |
| `UnidadeDeTrabalhoLancamentos` | `IUnidadeDeTrabalhoLancamentos.ConfirmarAsync`: um `SaveChanges` (transação). SQLSTATE 23505 em `ux_lancamentos_conta_chave_idempotencia`/`ux_lancamentos_estorno_de` volta como `ChaveIdempotenciaDuplicada`/`EstornoDuplicado` | EF Core + Npgsql |
| `ConsultasExtrato` | `IConsultasExtrato`: SQL direto pelo índice `(conta_id, ocorrido_em desc, id)` | EF Core `SqlQuery` |
| `PublicadorOutbox` | `BackgroundService` registrado só neste host. Lote de 100 pendentes (`ORDER BY ocorrido_em ... FOR UPDATE SKIP LOCKED`), publica com `mandatory` e confirms, marca `publicado_em`; falha ⇒ `tentativas++` e backoff 1 s → 30 s | RabbitMQ.Client 7 |
| `InicializadorBancoLancamentos`, `SemeadorLancamentos` | `MigrateAsync` com retry; 5 contas `CC-0001..CC-0005`; histórico de ~1 ano (`PlanoHistorico`) numa **única transação** junto com a marca `historico-lancamentos-v1` em `semeaduras` | EF Core |

## Relações

| Relação | Descrição |
|---|---|
| Portal → endpoints | Server Actions `registrar`/`estornar` enviam `Idempotency-Key` gerada ao abrir o formulário e reenviada em retentativas |
| Casos de uso → repositórios / caixa de saída / UoW | A Aplicacao só conhece portas; o lançamento e a mensagem de outbox entram no mesmo `SaveChanges` |
| UoW → database | A UNIQUE `(conta_id, chave_idempotencia)` decide corridas sem lock distribuído; a UNIQUE `estorno_de` impede segundo estorno |
| `PublicadorOutbox` → database / RabbitMQ | Desacoplado da requisição: o registro nunca espera o broker. O RabbitMQ **não** está no `/health/ready` — com ele fora, a API segue pronta e os eventos acumulam como pendentes |
| Seed → database | Passa pelo caso de uso `RegistrarHistoricoLancamentos`, então gera outbox e o consolidado do histórico é populado pelo fluxo de produção |
