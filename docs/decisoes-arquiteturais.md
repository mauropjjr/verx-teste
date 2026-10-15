# Decisões arquiteturais

Documento único com **todas** as decisões do design (D0–D16, mais D2.1), escrito depois da implementação: descreve
o que **foi construído**, as alternativas consideradas, por que esta escolha e o que ela custa. Onde o código
divergiu do design original, a divergência está
registrada na própria decisão e resumida em [Divergências entre design e implementação](#divergências-entre-design-e-implementação).

As decisões mais significativas também têm um ADR curto em [`docs/adr/`](adr/README.md). Diagramas em
[`docs/arquitetura/`](arquitetura/README.md); números medidos em [`docs/nfr.md`](nfr.md).

## O problema em quatro restrições

1. **Disponibilidade assimétrica:** o registro de lançamentos tem de continuar funcionando com o consolidado fora do
   ar (requisito central do desafio).
2. **Pico de leitura:** consolidado diário a 50 req/s com no máximo 5% de perda.
3. **Dinheiro:** nenhuma inconsistência pode chegar ao cliente — sem duplicar lançamento em retentativa, sem perder
   evento, sem arredondamento binário.
4. **Avaliação:** roda com um único `docker compose up --build` numa máquina qualquer, compila com zero warnings, e
   o raciocínio tem de ficar explícito.

Quase todas as decisões abaixo derivam de (1) e (3): separar escrita de leitura **entre processos** e tornar cada
passo assíncrono **idempotente**.

## Índice

| # | Decisão | ADR |
|---|---|---|
| [D0](#d0-nomes-em-português) | Nomes em português | — |
| [D1](#d1-estilo-arquitetural-microsserviços-enxutos-em-monorepo) | Estilo arquitetural: microsserviços enxutos em monorepo | [0001](adr/0001-microsservicos-enxutos-em-monorepo.md) |
| [D2](#d2-clean-architecture-organizada-por-camada-com-um-host-por-serviço) | Clean Architecture por camada, um host por serviço | [0002](adr/0002-clean-architecture-por-camada-com-hosts-separados.md) |
| [D2.1](#d21-repository--unit-of-work-só-no-lado-de-escrita) | Repository + Unit of Work só no lado de escrita | [0003](adr/0003-repository-e-unit-of-work-so-no-lado-de-escrita.md) |
| [D3](#d3-modelo-de-dados-e-dinheiro) | Modelo de dados e dinheiro (inclui "sem checagem de saldo") | [0008](adr/0008-sem-checagem-de-saldo-no-lancamento.md) |
| [D4](#d4-idempotência-na-api) | Idempotência na API | — |
| [D5](#d5-transactional-outbox) | Transactional Outbox | [0004](adr/0004-transactional-outbox-com-rabbitmq-client.md) |
| [D6](#d6-topologia-rabbitmq) | Topologia RabbitMQ | — |
| [D7](#d7-projeção-idempotente-e-independente-de-ordem) | Projeção idempotente e independente de ordem | [0005](adr/0005-projecao-idempotente-com-inbox-e-deltas-diarios.md) |
| [D8](#d8-retry-dlq-e-reprocessamento) | Retry, DLQ e reprocessamento | — |
| [D9](#d9-cache-e-desempenho-do-consolidado) | Cache e desempenho do consolidado | [0006](adr/0006-cache-aside-versionado-no-redis.md) |
| [D10](#d10-identidade-jwt-próprio-hoje-keycloak-amanhã) | Identidade: JWT próprio hoje, Keycloak amanhã | [0007](adr/0007-identidade-propria-com-jwt-rs256-e-keycloak-como-evolucao.md) |
| [D11](#d11-demais-controles-de-segurança) | Demais controles de segurança | — |
| [D12](#d12-portal-nextjs-como-bff) | Portal Next.js como BFF | — |
| [D13](#d13-migrações-e-seed-na-inicialização) | Migrações e seed na inicialização | [0009](adr/0009-migracoes-e-seed-na-inicializacao.md) |
| [D14](#d14-docker-compose-e-saúde) | Docker Compose e saúde | — |
| [D15](#d15-estratégia-de-testes) | Estratégia de testes | — |
| [D16](#d16-documentação-de-arquitetura) | Documentação de arquitetura | — |
| [Transversal](#consistência-eventual-como-contrato) | Consistência eventual como contrato | — |
| [Transversal](#observabilidade) | Observabilidade | — |
| [Transversal](#padrões-e-práticas-aplicados) | Padrões e práticas aplicados | — |

---

## D0. Nomes em português

**Contexto.** Pedido explícito do autor; o domínio (lançamento, estorno, consolidado, saldo) é bancário brasileiro e
os avaliadores leem português. Traduzir termos de negócio para inglês cria um segundo vocabulário que precisa ser
mapeado de cabeça.

**Decisão.** Classes, métodos, propriedades, variáveis, rotas, campos JSON, tabelas/colunas, filas e nomes de testes
em português sem acentos (`RegistrarLancamento`, `ConsolidadoDiario`, `contaId`, `mensagens_outbox`,
`consolidado.lancamento-registrado`). Ficam em inglês: APIs do framework (`DbContext`, `BackgroundService`,
`ExecuteAsync`), cabeçalhos padronizados (`Idempotency-Key`, `X-Correlation-Id`), sufixo `Async` e nomes de
padrão (`Outbox`, `Inbox`, `DLQ`).

**Alternativas consideradas.** Tudo em inglês (padrão de mercado, combina com o framework); termos de domínio em
português e técnicos em inglês sem regra escrita.

**Por que esta escolha.** A linguagem ubíqua fica igual no código, na API e na conversa com o negócio. A
regra escrita evita a mistura arbitrária.

**Consequências.** Mistura visível com APIs do framework (`await unidadeDeTrabalho.ConfirmarAsync(...)` ao lado de
`SaveChangesAsync`); quem vem de fora precisa da convenção. Sem acentos para não brigar com ferramentas.

**Onde está no código.** Todo o repositório; exemplos: `src/Banco.Aplicacao/Lancamentos/RegistrarLancamento.cs`,
`src/Banco.Infraestrutura/Compartilhado/Mensageria/TopologiaMensageria.cs`, rotas em `src/Banco.Api.*/Endpoints/`.

---

## D1. Estilo arquitetural: microsserviços enxutos em monorepo

**Contexto.** Restrição (1): uma falha ou pico no consolidado **não pode** afetar o registro de lançamentos. Ao mesmo
tempo, o prazo é curto e tudo precisa subir na máquina do avaliador.

**Decisão.** Três serviços de domínio + um worker, cada um em seu processo/container, num único repositório:

- `identidade-api` — login e emissão de JWT;
- `lancamentos-api` — lado de escrita, fonte de verdade, não conhece o Consolidado;
- `consolidado-api` (leitura) e `consolidado-worker` (projeção) — processos separados que escalam de forma independente;
- comunicação entre Lançamentos e Consolidado **só** por evento (`LancamentoRegistradoV1` via RabbitMQ);
- **um database por serviço** (`identidade`, `lancamentos`, `consolidado`) na mesma instância Postgres local, sem
  acesso cruzado.

Diagramas: [containers](arquitetura/02-containers.md).

**Alternativas consideradas.**

| Alternativa | A favor | Contra |
|---|---|---|
| Monólito modular (um processo) | Uma implantação, transações locais, depuração simples | Pico ou falha do consolidado degradam o mesmo processo que registra lançamentos — contraria a restrição (1). Um bug de memória na leitura derruba a escrita |
| Monólito + réplica de leitura | Separa I/O de banco | Continua um processo só; não isola CPU, threads nem falhas de código |
| Microsserviços completos (gateway, service mesh, um repositório por serviço) | Isolamento máximo | Peças demais para o prazo e para a máquina do avaliador, sem ganho para os requisitos |

**Por que esta escolha.** É o menor número de processos que satisfaz (1): escrita e leitura em processos diferentes,
ligados por mensageria durável. A Identidade é separada porque as APIs só dependem da chave **pública** dela
(cacheada): Identidade fora impede novos logins, mas não derruba sessões já emitidas.

**Consequências / trade-offs.**

- Mais operação: 4 processos .NET, broker e cache. Assumido pelo requisito de isolamento.
- Consistência **eventual** entre escrita e leitura (veja [seção transversal](#consistência-eventual-como-contrato)).
- Um único Postgres local é ponto único de falha **do ambiente de avaliação**; os databases separados mantêm a
  fronteira lógica e permitem separar instâncias depois (veja [evolução](evolucao.md)).
- Comprovação: `tests/Banco.TestesSistema/RecuperacaoConsolidadoTestes.cs` para worker e API do consolidado, registra
  100 lançamentos (todos `201`) e verifica a convergência quando voltam ([nfr.md](nfr.md)).

**Onde está no código.** `docker-compose.yml`; hosts `src/Banco.Api.Identidade`, `src/Banco.Api.Lancamentos`,
`src/Banco.Api.Consolidado`, `src/Banco.Worker.Consolidado`; `infra/postgres/01-criar-databases.sql`.

---

## D2. Clean Architecture organizada por camada, com um host por serviço

**Contexto.** Com quatro hosts, é preciso decidir como organizar o código .NET: um conjunto de projetos por serviço
ou um projeto por camada compartilhado entre os hosts. Regras de negócio precisam ser testáveis sem banco, e
Lançamentos/Consolidado não podem se acoplar por código.

**Decisão (do autor).** Clean Architecture com **um projeto por camada** — `Banco.Dominio`, `Banco.Aplicacao`,
`Banco.Infraestrutura` — e, dentro de cada um, pastas/namespaces por contexto: `Compartilhado`, `Identidade`,
`Lancamentos`, `Consolidado`. Quatro hosts finos (`Banco.Api.*`, `Banco.Worker.Consolidado`), cada um compondo
**apenas** o seu contexto pelas extensões de DI (`AdicionarInfraestruturaLancamentos`,
`AdicionarConsultasConsolidado`, `AdicionarConsumidorConsolidado`, ...).

- Regra de dependência: `Hosts → Infraestrutura → Aplicacao → Dominio` e `Hosts → Aplicacao`.
- `Banco.Dominio`: nenhum pacote. `Banco.Aplicacao`: só o Dominio (casos de uso, uma classe com `ExecutarAsync`, e
  portas). `Banco.Infraestrutura`: EF Core/Npgsql, RabbitMQ.Client, StackExchange.Redis, Polly, Serilog, JwtBearer.
  Hosts: HTTP/mensagem ⇄ caso de uso, validação de formato (FluentValidation) e composição.
- Casos de uso chamados diretamente, sem MediatR (licença comercial a partir da v13 e indireção desnecessária aqui).
- `Directory.Build.props`: `TreatWarningsAsErrors`, `Nullable`, `AnalysisLevel latest-recommended`,
  `EnforceCodeStyleInBuild`; `Directory.Packages.props` com versões centralizadas.

Diagrama da estrutura e da regra de dependência: [arquitetura/README.md](arquitetura/README.md#organização-do-código-clean-architecture-por-camada-um-host-por-serviço).

**Alternativas consideradas.**

| Alternativa | A favor | Contra |
|---|---|---|
| (a) Projetos por serviço (`src/Lancamentos/{Dominio,Aplicacao,Infraestrutura,Api}`, idem para os outros) | Isolamento **físico**: o compilador impede o acoplamento; cada imagem leva só o código do seu serviço; extrair para outro repositório é mover uma pasta | ~13–16 projetos para um sistema pequeno; mais `csproj` e referências para manter; navegação por camada fica espalhada |
| (b) Monólito modular com um único host | Mais simples ainda | Lançamentos e Consolidado no mesmo processo — contraria D1 |
| (c) Sem camadas (endpoints falando com `DbContext`) | Menos código | Regras de negócio misturadas com EF e HTTP; Aplicacao não testável sem banco |

**Por que esta escolha.** Menos projetos, navegação simples ("onde estão os casos de uso?" → `Banco.Aplicacao`) e os
processos continuam independentes, que é o que o requisito de disponibilidade exige. A fronteira entre contextos,
que a alternativa (a) daria de graça pelo compilador, é recuperada com dois mecanismos baratos.

**Consequências / trade-offs.**

- **Cada imagem carrega o código e os pacotes de todos os contextos** (ex.: a imagem da Identidade contém o cliente
  RabbitMQ e o Redis, que nunca usa). Imagens maiores e superfície de dependências maior do que o necessário.
- Uma mudança em uma camada recompila e reimplanta **todos** os hosts.
- O isolamento lógico deixa de ser garantido pela fronteira de projeto e passa a depender de:
  1. **testes de arquitetura** (NetArchTest, inspecionando o IL): Dominio sem dependências; Aplicacao só com o
     Dominio; para cada camada × contexto, nenhum tipo usa outro contexto — só `Compartilhado`;
  2. **composição por host**: cada `Program.cs` só registra as extensões do seu contexto, então nem em tempo de
     execução um host resolve serviço de outro contexto.
- Extrair um serviço para repositório próprio exige mover as pastas do contexto para novos projetos — trabalho
  mecânico, facilitado justamente pelo isolamento testado (veja [evolução](evolucao.md)).

**Onde está no código.** `src/Banco.{Dominio,Aplicacao,Infraestrutura}/{Compartilhado,Identidade,Lancamentos,Consolidado}/`;
`src/Banco.*/Program.cs`; `src/Banco.Infraestrutura/*/Extensoes*.cs`;
`tests/Banco.TestesUnitarios/Arquitetura/RegrasDeArquiteturaTestes.cs`; `Directory.Build.props`, `Directory.Packages.props`.

---

## D2.1 Repository + Unit of Work só no lado de escrita

**Contexto.** A Aplicacao não pode depender de EF Core (D2), mas precisa gravar o lançamento **e** a mensagem de
outbox — e, no Consolidado, o inbox **e** a projeção — na mesma transação.

**Decisão.**

- **Lado de escrita — adotados.** Um repositório por agregado (sem repositório genérico CRUD) e uma unidade de
  trabalho por contexto, como portas na Aplicacao:
  - Identidade: `IRepositorioUsuarios`, `IUnidadeDeTrabalhoIdentidade`;
  - Lançamentos: `IRepositorioContas`, `IRepositorioLancamentos`, `ICaixaSaidaEventos`,
    `IUnidadeDeTrabalhoLancamentos` (`ConfirmarAsync` devolve `ResultadoConfirmacao`: `Confirmado`,
    `ChaveIdempotenciaDuplicada`, `EstornoDuplicado` — a violação da UNIQUE vira resultado, não exceção);
  - Consolidado (projeção): `IRegistroMensagensProcessadas`, `IRepositorioResumosDiarios`,
    `IUnidadeDeTrabalhoConsolidado` (transação explícita `IniciarAsync`/`ConfirmarAsync`, porque a projeção é SQL
    direto, não `SaveChanges`).
- **Lado de leitura — sem repositório.** Portas de consulta com SQL direto: `IConsultasExtrato`
  (`ConsultasExtrato`) e `IConsultasConsolidado` (`ConsultasConsolidado`, atrás do decorator de cache).

**Alternativas consideradas.** (a) Usar o `DbContext` direto na Aplicacao — ele já é um Unit of Work e os `DbSet`
já são repositórios; (b) repositório genérico `IRepositorio<T>` com CRUD; (c) repositório também na leitura.

**Por que esta escolha.**

- **Por que não só `DbContext`:** seria mais simples e o próprio time do EF recomenda em muitos casos. Mas a
  Aplicacao passaria a depender de EF Core (quebra a regra de dependência, que é testada) e os casos de uso só
  seriam testáveis com banco. A abstração aqui é fina — uma interface por agregado com 2 a 4 métodos — e paga o
  custo com testes de Aplicacao sem infraestrutura e com a tradução de `23505` para um resultado de negócio.
- **Por que não genérico:** `IRepositorio<T>.Atualizar` não faz sentido para lançamentos imutáveis; um repositório por
  agregado expõe só o que o agregado admite (`IRepositorioLancamentos` só tem `Adicionar`).
- **Por que não na leitura:** repositórios orientados a agregado atrapalham consultas — o consolidado de um período
  é uma *window function* sobre `generate_series`, não uma coleção de agregados. É CQRS também **dentro** do serviço.

**Consequências / trade-offs.** Mais interfaces e classes de infraestrutura; duas formas de acesso a dados no mesmo
contexto (repositório na escrita, SQL na leitura) que o leitor precisa entender — por isso esta seção existe. A
`UnidadeDeTrabalhoLancamentos` é o ponto que garante o outbox (D5) sem a Aplicacao conhecer EF.

**Onde está no código.** Portas em `src/Banco.Aplicacao/*/Portas/`; implementações em
`src/Banco.Infraestrutura/Lancamentos/Persistencia/` (`RepositorioContas`, `RepositorioLancamentos`,
`UnidadeDeTrabalhoLancamentos`, `ConsultasExtrato`), `src/Banco.Infraestrutura/Identidade/Persistencia/`,
`src/Banco.Infraestrutura/Consolidado/Persistencia/` (`RepositoriosProjecao.cs`, `ConsultasConsolidado.cs`).

---

## D3. Modelo de dados e dinheiro

**Contexto.** Restrição (3): dinheiro não pode sofrer arredondamento binário; um lançamento registrado é um fato
contábil.

**Decisão.**

- `decimal` no .NET e `numeric(18,2)` no Postgres; valor **sempre positivo** + `tipo` `CREDITO`/`DEBITO`. Nunca `double`.
- `lancamentos`: `id` UUID v7 (`Guid.CreateVersion7(instante)`, ordenável no tempo), `conta_id`, `tipo`, `valor`,
  `descricao`, `ocorrido_em timestamptz` (relógio do servidor), `estorno_de` (UNIQUE), `chave_idempotencia`,
  `hash_requisicao`; UNIQUE `(conta_id, chave_idempotencia)`; índice `(conta_id, ocorrido_em, id)` para o extrato.
- Lançamentos **imutáveis**; correção só por **estorno compensatório** (lançamento de tipo oposto apontando para o
  original); um estorno não pode ser estornado; um lançamento só pode ser estornado uma vez.
- **Sem checagem de saldo suficiente:** débito é aceito mesmo deixando a conta negativa ([ADR 0008](adr/0008-sem-checagem-de-saldo-no-lancamento.md)).

**Alternativas consideradas.** Valor com sinal (um campo só); `UPDATE`/`DELETE` para corrigir; validar saldo no
registro (consultando o consolidado ou somando o razão).

**Por que esta escolha.** Valor positivo + tipo é o padrão contábil e evita sinal invertido por engano. Imutabilidade +
estorno preserva a trilha de auditoria. A checagem de saldo **acoplaria a escrita à leitura** (o consolidado é
eventual e pode estar fora — exatamente o que D1 evita) ou exigiria serializar todos os lançamentos de uma conta;
limite, cheque especial e bloqueio pertencem a um domínio de autorização transacional que não faz parte do desafio.

**Consequências.** Contas podem ficar negativas; o extrato mostra o original e o estorno; o saldo é sempre derivado
(nunca armazenado no lado de escrita).

**Onde está no código.** `src/Banco.Dominio/Lancamentos/{Lancamento,Conta,TipoLancamento}.cs`;
`src/Banco.Infraestrutura/Lancamentos/Persistencia/LancamentosDbContext.cs` e migrações em `Migracoes/`.

---

## D4. Idempotência na API

**Contexto.** Retentativa de rede ou duplo clique não podem criar dois lançamentos.

**Decisão.** `Idempotency-Key` **obrigatório** em registro e estorno (ausente ⇒ `400`). O caso de uso calcula
`hash_requisicao` (SHA-256 do corpo normalizado), procura lançamento com a mesma chave na conta: mesmo hash ⇒ devolve
o original (`201`, mesmo id); hash diferente ⇒ `422`. Na corrida, ambos passam pela busca, um `INSERT` vence e o outro
recebe `23505` em `ux_lancamentos_conta_chave_idempotencia`, que a unidade de trabalho traduz em
`ChaveIdempotenciaDuplicada`; o perdedor lê o vencedor e responde como reenvio. No portal, a chave é gerada ao abrir
o formulário e o botão fica desabilitado durante o envio. Fluxo: [04-fluxos §2](arquitetura/04-fluxos.md#2-registro-de-lançamento-com-idempotência).

**Alternativas consideradas.** Chave no Redis com TTL; lock distribuído; idempotência opcional.

**Por que esta escolha.** A UNIQUE no mesmo banco e na mesma transação do lançamento é a única forma que não tem
janela de inconsistência: o Redis não é transacional com o Postgres (gravar a chave e falhar o lançamento, ou o
contrário). Sem lock distribuído, sem nova peça.

**Consequências.** A chave vive para sempre (não expira); a mesma chave em contas diferentes são lançamentos
diferentes (escopo por conta). O cliente precisa reenviar a **mesma** chave em retentativas — o portal faz isso.

**Onde está no código.** `src/Banco.Aplicacao/Lancamentos/{Idempotencia,HashRequisicao,RegistrarLancamento,EstornarLancamento}.cs`;
`UnidadeDeTrabalhoLancamentos.cs`; teste `tests/Banco.TestesIntegracao.Lancamentos/IdempotenciaTestes.cs` (20
requisições concorrentes com a mesma chave ⇒ um lançamento, mesmo id em todas).

---

## D5. Transactional Outbox

**Contexto.** Gravar no banco e publicar no broker são duas operações sem transação comum (*dual write*): publicar
antes do COMMIT pode anunciar um lançamento que não existe; publicar depois pode perder o evento num crash. E o
registro não pode depender do broker estar no ar.

**Decisão.** O evento `LancamentoRegistradoV1` é gravado em `mensagens_outbox(id, tipo, conteudo jsonb, id_correlacao,
ocorrido_em, publicado_em, tentativas)` **na mesma transação** do lançamento, pela porta `ICaixaSaidaEventos`
(`CaixaSaidaOutbox`). O `PublicadorOutbox` (BackgroundService registrado só na `lancamentos-api`) lê lotes de 100
pendentes com `FOR UPDATE SKIP LOCKED` (réplicas dividem o trabalho), publica com **publisher confirms** e
`mandatory`, e só então marca `publicado_em`; falha ⇒ `tentativas++` e backoff exponencial de 1 s até 30 s.
**Id da mensagem = id do lançamento = `message_id` no RabbitMQ**: cada lançamento gera exatamente um evento, a PK do
outbox impede duplicar e o consumidor deduplica por esse id. O RabbitMQ **não** entra no `/health/ready` da API.
Fluxo: [04-fluxos §3](arquitetura/04-fluxos.md#3-outbox--rabbitmq).

**Alternativas consideradas.**

| Alternativa | Por que não |
|---|---|
| Publicar direto na requisição | *Dual write*; broker fora derruba o registro — contraria D1 |
| CDC (Debezium) lendo o WAL | Mais uma peça pesada (Kafka Connect) para a máquina do avaliador |
| MassTransit (outbox pronto) | Licença comercial a partir da v9 |
| NServiceBus, CAP, Wolverine | Escondem exatamente as decisões que a avaliação quer ver; NServiceBus é comercial |

**Por que esta escolha.** O padrão é pequeno (uma tabela, um laço) e, escrito com `RabbitMQ.Client` direto, deixa
explícitos confirms, `SKIP LOCKED`, backoff e o que acontece em cada falha.

**Consequências.** *At-least-once*: queda entre o ack do broker e o COMMIT republica a mensagem — aceito, o inbox (D7)
neutraliza. Ordem não garantida entre lotes/réplicas — o consumidor não depende dela. Latência extra de até ~1 s
(intervalo de sondagem). A tabela de outbox cresce: falta uma rotina de limpeza das mensagens publicadas
([evolução](evolucao.md)).

**Onde está no código.** `src/Banco.Infraestrutura/Lancamentos/Outbox/{CaixaSaidaOutbox,MensagemOutbox,PublicadorOutbox,OpcoesPublicadorOutbox}.cs`;
`src/Banco.Infraestrutura/Lancamentos/ExtensoesInfraestruturaLancamentos.cs` (`AdicionarPublicadorOutboxLancamentos`);
testes `OutboxTestes.cs`, `PublicacaoOutboxTestes.cs` (broker fora ⇒ `201`, API pronta, mensagem pendente e publicada
na volta com `message_id` e correlação).

---

## D6. Topologia RabbitMQ

**Contexto.** O evento precisa sobreviver a reinício do broker e mensagens problemáticas não podem bloquear a fila.

**Decisão.** Exchange `lancamentos.eventos` (topic, durável) → routing key `lancamento.registrado.v1` → fila
`consolidado.lancamento-registrado` (**quorum**, durável) com DLX `consolidado.dlx` (fanout) →
`consolidado.lancamento-registrado.dlq` (quorum). Mensagens persistentes, `message_id` = id do lançamento, cabeçalho
`x-correlation-id`, `content-type: application/json`. Consumidor com prefetch configurável (20) e *manual ack*. A
topologia é declarada de forma idempotente por quem publica e por quem consome, a partir de uma única classe.

**Alternativas consideradas.** Fila clássica; Kafka; exchange direct; filas declaradas por script de infraestrutura.

**Por que esta escolha.** Quorum queue é a fila replicável recomendada no RabbitMQ 4 (pronta para cluster).
Topic exchange permite que outros consumidores (contabilidade, antifraude) liguem suas filas sem tocar no publicador.
Kafka traria log reprocessável, mas mais peso operacional sem necessidade de *replay* nesta entrega. Declarar no
código garante que a topologia existe antes do primeiro publish, inclusive em testes.

**Consequências.** Mudar um argumento de fila já existente é recusado pelo broker (`PRECONDITION_FAILED`) — por isso os
argumentos vivem só em `TopologiaMensageria`. Um broker só, sem HA, no ambiente local ([evolução](evolucao.md)).

**Onde está no código.** `src/Banco.Infraestrutura/Compartilhado/Mensageria/{TopologiaMensageria,ConexaoRabbitMq,OpcoesRabbitMq}.cs`;
`src/Banco.Aplicacao/Compartilhado/Contratos/LancamentoRegistradoV1.cs`.

---

## D7. Projeção idempotente e independente de ordem

**Contexto.** Com *at-least-once* (D5) e sem ordem garantida, o consumidor recebe duplicatas e eventos fora de ordem;
o saldo não pode dobrar nem depender da ordem de chegada.

**Decisão.** `ProjetarLancamento`, numa única transação (`IUnidadeDeTrabalhoConsolidado`):

1. `INSERT INTO mensagens_processadas (id_mensagem) ... ON CONFLICT DO NOTHING` — **inbox**; 0 linhas ⇒ duplicata,
   nada muda;
2. UPSERT em `resumos_diarios(conta_id, dia)` com `creditos = creditos + x`, `debitos = debitos + y` — só **deltas
   diários**, somas comutativas;
3. `INSERT lancamentos_projetados` (para o saldo intradiário);
4. UPSERT `marcas_atualizacao` com `GREATEST(ultimo_ocorrido_em)` — fonte do `atualizadoAte`.

Depois do COMMIT, incrementa `versao:{conta}` no Redis (D9) — também na duplicata, para cobrir uma queda entre o
COMMIT e o Redis. Leitura: fechamento de D = soma dos deltas até D; saldo em `em` = deltas dos dias anteriores +
movimentos do dia até `em`. Fluxo: [04-fluxos §4](arquitetura/04-fluxos.md#4-projeção-no-worker-inbox--upsert--versão).

**Alternativas consideradas.** Snapshot de **saldo acumulado** por dia (leitura O(1)); deduplicar por cache/Redis;
exigir ordem (fila com consumidor único, *sequence number*).

**Por que esta escolha.** Com saldo acumulado por dia, um evento atrasado reescreve todos os dias seguintes —
escrita O(dias) e sensível à ordem. Com deltas, a escrita é O(1) e comutativa; a leitura fica O(dias), compensada por
índice, cache e (no futuro) snapshots mensais. Inbox na mesma transação dá **efeito exactly-once** sem coordenação
distribuída; o Redis não serviria (não é transacional com o Postgres).

**Consequências.** `mensagens_processadas` cresce indefinidamente (evolução: expurgo por idade maior que a janela de
retenção da fila). Leitura de período longo custa a soma dos dias anteriores — medido em [nfr.md](nfr.md) com 1 ano
de dados.

**Divergência.** O design previa a `CalculadoraSaldo` (domínio) compondo o fechamento a partir dos resumos
carregados. A implementação calcula **no SQL** (`SUM(...) OVER (ORDER BY dia)` sobre `generate_series`, uma ida ao
banco por consulta). A `CalculadoraSaldo` permanece como especificação executável das regras (testes unitários), sem
ser usada pelas consultas.

**Onde está no código.** `src/Banco.Aplicacao/Consolidado/ProjetarLancamento.cs`, `Portas/PortasProjecao.cs`;
`src/Banco.Infraestrutura/Consolidado/Persistencia/{RepositoriosProjecao,ConsultasConsolidado,ConsolidadoDbContext}.cs`;
`src/Banco.Dominio/Consolidado/{ResumoDiario,Movimento,CalculadoraSaldo}.cs`; testes `ProjecaoLancamentoTestes.cs`
(duplicata, fora de ordem, Redis fora), `ConsolidadoDiarioTestes.cs`, `SaldoTestes.cs`, `CalculadoraSaldoTestes.cs`.

---

## D8. Retry, DLQ e reprocessamento

**Contexto.** Falhas transitórias (banco reiniciando) não devem mandar mensagens para a DLQ; mensagens envenenadas
não podem travar a fila; e o que for para a DLQ precisa voltar sem intervenção manual no broker.

**Decisão.** `ProcessadorMensagemLancamento` aplica retry Polly (3 novas tentativas, backoff exponencial com jitter a
partir de 1 s) **só** para falhas que `ClassificadorFalhas` considera transitórias (`NpgsqlException.IsTransient`,
timeout); cada tentativa num escopo DI novo. JSON inválido, `EventoInvalidoException` ou tentativas esgotadas ⇒
`nack(requeue: false)` ⇒ DLX ⇒ DLQ, e a fila segue. `POST /api/v1/admin/dlq/reprocessar?maximo=N` (papel `admin`,
1 ≤ N ≤ 1000, padrão 100) move as mensagens uma a uma: `basic.get` → publish na exchange principal com routing key e
propriedades originais → confirm → ack; falha ⇒ nack com requeue (volta para a DLQ). Conexões com *automatic
recovery*. Fluxo: [04-fluxos §7](arquitetura/04-fluxos.md#7-dlq-e-reprocessamento).

**Alternativas consideradas.** Requeue infinito (trava a fila em mensagem envenenada); filas de retry com TTL
(*delayed retry* no broker); shovel manual pelo management.

**Por que esta escolha.** Retry em memória cobre o caso comum (segundos de indisponibilidade) sem topologia extra;
a DLQ isola o resto; o endpoint torna o reprocessamento auditável e restrito a `admin`. O inbox (D7) torna o
reprocessamento seguro mesmo que a mensagem já tenha sido projetada.

**Consequências.** Durante o retry a mensagem ocupa um slot do prefetch (até ~7 s). Não há alerta automático de DLQ
não vazia — evolução com métricas.

**Onde está no código.** `src/Banco.Infraestrutura/Consolidado/Mensageria/{ProcessadorMensagemLancamento,ClassificadorFalhas,ConsumidorLancamentosRegistrados,FilaMensagensMortasRabbitMq,OpcoesConsumidorConsolidado}.cs`;
`src/Banco.Aplicacao/Consolidado/ReprocessarDlq.cs`; `src/Banco.Api.Consolidado/Endpoints/EndpointsAdministracao.cs`;
testes `ConsumidorLancamentosTestes.cs`, `ReprocessamentoDlqTestes.cs`.

---

## D9. Cache e desempenho do consolidado

**Contexto.** Restrição (2): 50 req/s com ≤ 5% de perda. A leitura é O(dias) (D7). Invalidar cache por padrão de
chave (`SCAN` + `DEL`) é caro e sujeito a corrida.

**Decisão.** Cache-aside no Redis implementado como **decorator** de `IConsultasConsolidado`
(`ConsultasConsolidadoEmCache` → `ConsultasConsolidado`), invisível para os casos de uso:

- chaves `consolidado:{conta}:{versao}:{de}:{ate}` e `saldo:{conta}:{versao}:{em}`; o worker faz `INCR
  versao:{conta}` a cada projeção ⇒ dado novo = chave nova, **sem `SCAN`/`DEL`**;
- TTL de 10 min se o intervalo só tem dias passados, 5 s se inclui hoje (UTC);
- saldo sem `em` usa "agora" **truncado ao segundo**, para requisições próximas compartilharem a chave;
- Redis fora: `CircuitoCacheConsolidado` (Polly, abre com 50% de falhas, mínimo 2 em 10 s, por 15 s) + timeouts de
  500 ms e `BacklogPolicy.FailFast` ⇒ a consulta vai ao Postgres sem esperar; `/health/ready` responde `503`;
- SQL de uma ida ao banco por consulta, servido pela PK `(conta_id, dia)` e pelo índice `(conta_id, ocorrido_em)`.

Fluxo: [04-fluxos §5](arquitetura/04-fluxos.md#5-consulta-do-consolidado-com-cache).

**Alternativas consideradas.** Invalidação explícita por `DEL`/pub-sub; cache em memória por réplica
(`IMemoryCache`); materializar o saldo acumulado (veja D7); sem cache.

**Por que esta escolha.** A versão por conta torna a invalidação atômica e barata, sem conhecer quais chaves existem.
Cache distribuído é compartilhado entre réplicas da API. O decorator mantém o cache como detalhe de infraestrutura.

**Consequências — o que a medição mostrou ([nfr.md](nfr.md)).**

- Meta do requisito atendida: perda entre 0,00% e 0,40% em todos os cenários com o sistema aquecido, 0 respostas de
  erro, nenhum `429`.
- **p95 < 300 ms (meta de engenharia) não é estável com escrita concorrente:** 85,9 ms no cenário normal, 31,7 ms com
  Redis parado, mas 276,6 ms e 320,1 ms com 10 lançamentos/s em paralelo (e 416,9 ms numa rodada não arquivada) — tudo
  dividindo 4 vCPU num notebook com o próprio k6. Com Redis parado o Postgres sozinho respondeu em p95 de 31,7 ms: o
  aumento vem de contenção de CPU/E-S causada pela escrita, não do custo da consulta.
- **Nesta escala o cache não é o que garante a latência:** com ~1,8 mil resumos diários o Postgres responde em poucos
  ms e o caminho com Redis (dois `GET` + serialização + `SET` nos misses) chega a ser mais lento. A invalidação por
  versão funciona como projetado: o acerto cai de ~71% para ~30% quando cada conta recebe ~2 lançamentos/s, porque
  qualquer projeção invalida também os períodos passados da conta. O cache protege o Postgres conforme o histórico
  cresce; a versão por conta é grossa (evolução: versão por mês ou snapshots).
- **Partida a frio:** logo após `restart` da API, p95 de 1.975 ms e perda de 2,50% (ainda dentro da meta), com o
  Postgres recusando conexões (`53300: sorry, too many clients already`) enquanto requisições represadas abriam
  conexões novas. Mitigações (limite de pool abaixo de `max_connections` somado, PgBouncer, aquecimento antes do
  *ready*) estão em [evolução](evolucao.md).

**Onde está no código.** `src/Banco.Infraestrutura/Consolidado/Cache/*`; `src/Banco.Aplicacao/Consolidado/ConsultarConsolidado.cs`
(`ConsultarSaldo.AgoraAoSegundo`); `tests/carga/consolidado.js`; sumários em [`docs/teste-carga/`](teste-carga/);
teste `CacheConsolidadoTestes.cs`.

---

## D10. Identidade: JWT próprio hoje, Keycloak amanhã

**Contexto.** As APIs precisam de autenticação e autorização por papel. Um banco real usaria um provedor de
identidade (IdP) corporativo com OIDC, MFA e federação; mas o ambiente precisa subir rápido e sem configuração manual
na máquina do avaliador. Pedido explícito do autor: login próprio com JWT nesta entrega.

**Decisão (hoje).**

- Serviço `identidade-api` próprio: tabela `usuarios` (login, nome, hash, papéis, `tentativas_falhas`,
  `bloqueado_ate`); hash com o `PasswordHasher` do ASP.NET Core Identity (PBKDF2-HMAC-SHA512, 100 mil iterações,
  salt) **sem** adotar os stores do Identity; bloqueio após 5 falhas por 15 min; rate limit do login por IP (30/min);
  mensagem genérica (não revela se o usuário existe).
- Token **JWT RS256**, 30 min, `iss=banco-identidade`, `aud=banco-api`, `sub`, `name`, uma claim `role` por papel,
  `kid` (thumbprint JWK) no header. Par RSA 2048 gerado na primeira subida e persistido no volume
  `identidade-chaves`, ou lido de `Identidade__Chaves__ChavePrivadaPem`.
- Publicação em `/.well-known/openid-configuration` (mínimo) e `/.well-known/jwks.json`.
- APIs: `AddJwtBearer` com `MetadataAddress` vindo de **`Autenticacao:EnderecoMetadados`**
  (`http://identidade-api:8080/.well-known/openid-configuration`), validando emissor, audiência, assinatura (só
  RS256) e expiração (tolerância de 30 s); `MapInboundClaims = false`. Toda rota exige autenticação por padrão
  (*fallback policy*); policies `PodeConsultar` (consulta/operador/admin), `PodeOperar` (operador/admin),
  `PodeAdministrar` (admin).
- Sem refresh token: a sessão expira em 30 min e o usuário refaz o login.

**Alternativas consideradas.**

| Alternativa | Por que não agora |
|---|---|
| Keycloak no compose | Mais um container pesado (tempo de subida e memória na máquina do avaliador), realm a importar, e o problema clássico de **hostname dentro × fora do Docker**: o emissor visto pelo navegador (`localhost:8080`) difere do visto pelas APIs (`keycloak:8080`), quebrando a validação do `iss` sem configuração extra |
| HS256 com segredo compartilhado | Espalha o segredo que **assina** tokens por todos os serviços; qualquer serviço comprometido emite tokens |
| ASP.NET Core Identity completo | Traz stores, UI e tabelas que não seriam usados; só o hasher interessa |
| Sem autenticação | Inaceitável para dados financeiros |

**Por que esta escolha.** Entrega autenticação real (hash forte, bloqueio, assinatura assimétrica, papéis) com uma peça
pequena, e — o ponto principal — as APIs **já validam tokens do jeito que validariam os de um IdP OIDC**: discovery
+ JWKS + `iss`/`aud`. O código de negócio só conhece *policies*.

**Como migrar para o Keycloak (sem mudar regra de negócio).**

1. Subir o Keycloak com um realm `banco`, clients `banco-portal` (confidencial, **Authorization Code + PKCE**) e
   audiência `banco-api`; papéis de realm ou de client `consulta`, `operador`, `admin`; MFA (TOTP/WebAuthn) por
   política; federação com AD/LDAP; refresh token e revogação/logout de sessão pelo IdP.
2. Nas APIs, trocar configuração: `Autenticacao:EnderecoMetadados` →
   `https://<keycloak>/realms/banco/.well-known/openid-configuration`, `Autenticacao:Emissor` → URL do realm,
   `Autenticacao:Audiencia` → `banco-api`, `Autenticacao:ExigirHttpsMetadados=true`.
3. Mapear papéis: o Keycloak põe papéis em `realm_access.roles` (ou `resource_access.<client>.roles`), não em `role`.
   Ou um *protocol mapper* no Keycloak emite a claim `role` plana, ou um `IClaimsTransformation` nas APIs copia os
   papéis para `role`. As policies (`PodeConsultar`, ...) não mudam.
4. No portal, trocar a Server Action `entrar` pelo fluxo Authorization Code + PKCE (redirect ao Keycloak, troca do
   código no servidor) e guardar tokens no mesmo cookie httpOnly; o `proxy.ts` segue igual.
5. Desligar a `identidade-api` (ou mantê-la apenas para migração de usuários).

**Consequências.** Sem MFA, sem refresh, sem revogação antes da expiração (mitigado por token curto); chave RSA local
(produção: cofre/HSM com rotação — o `kid` já suporta múltiplas chaves no JWKS). Identidade fora impede novos logins,
mas tokens emitidos continuam válidos nas APIs (chaves cacheadas).

**Divergência.** O design chamava a configuração de `Authority`; o código usa a chave `Autenticacao:EnderecoMetadados`
(mapeada para `MetadataAddress`), com `Emissor` e `Audiencia` separados — o que torna a troca de IdP explícita.

**Onde está no código.** `src/Banco.Api.Identidade/Endpoints/`; `src/Banco.Aplicacao/Identidade/`;
`src/Banco.Infraestrutura/Identidade/{Tokens,Senhas,Persistencia}/`;
`src/Banco.Infraestrutura/Compartilhado/Autenticacao/{ExtensoesAutenticacao,PoliticasAutorizacao,Papeis}.cs`;
testes `LoginTestes.cs`, `ChavePublicaTestes.cs` (token aceito por uma API cujo JwtBearer só conhece o `MetadataAddress`),
`AutenticacaoAutorizacaoTestes.cs`, `EmissorTokenTestes.cs`, `UsuarioTestes.cs`.

---

## D11. Demais controles de segurança

**Contexto.** Dados financeiros e pessoais; abuso de API; logs são vetor clássico de vazamento.

**Decisão (implementado).**

- **Rate limiting:** limitador global *token bucket* particionado pelo `sub` do token (anônimo ⇒ IP), padrão 100
  req/s (`LimiteTaxa:RequisicoesPorSegundo`); login 30/min por IP; excedeu ⇒ `429` com `Retry-After`. Aplicado
  **depois** da autenticação para particionar pelo usuário.
- **Validação de entrada:** FluentValidation nos hosts (formato), regras de negócio no domínio; erros em Problem
  Details.
- **Erros:** Problem Details padronizado; exceções inesperadas respondem genérico com o id de correlação, sem stack
  trace.
- **Logs:** Serilog JSON com `FiltroDadosSensiveis`, que mascara `Authorization`, senha, tokens e cookies em qualquer
  nível de aninhamento; `ComandoAutenticar.ToString()` não inclui a senha.
- **Dados pessoais:** titular mascarado nas respostas (`Conta.TitularMascarado`); o evento `LancamentoRegistradoV1`
  não carrega titular nem descrição.
- **Superfície:** o navegador só fala com o Next.js; as APIs não habilitam CORS (chamadas cross-origin do navegador
  são bloqueadas por padrão). Token em cookie httpOnly (D12).
- **Segredos:** por variável de ambiente com defaults só de desenvolvimento (`.env.example`); chave privada fora do
  repositório (volume ou perfil local do usuário).

**Alternativas consideradas.** Rate limit no gateway (não há gateway); limitar por IP apenas (penaliza NAT
corporativo); logs sem filtro e confiar na disciplina.

**Por que esta escolha.** Controles nativos do ASP.NET Core, sem peça nova, testados unitariamente.

**Consequências / divergência.** O design citava "headers de segurança" (HSTS, `X-Content-Type-Options`, CSP) — **não
foram implementados** nas APIs nem no portal; ficam para a evolução junto com TLS de produção. Sem TLS no ambiente
local (rede interna do compose).

**Onde está no código.** `src/Banco.Infraestrutura/Compartilhado/{LimiteTaxa,Erros,Registro,Correlacao}/`;
`src/Banco.Api.*/Endpoints/ValidacaoEntrada.cs`; testes `LimiteTaxaTestes.cs`, `FiltroDadosSensiveisTestes.cs`,
`TratamentoErrosECorrelacaoTestes.cs`.

---

## D12. Portal Next.js como BFF

**Contexto.** O portal precisa de login próprio, operações por papel e degradação graciosa, sem expor o token ao
JavaScript do navegador (XSS).

**Decisão.** Next.js 16 (App Router, Server Components, `output: "standalone"`) como **BFF**: a Server Action
`entrar` chama a Identidade e grava o JWT no cookie **`sessao`** (httpOnly, `SameSite=Lax`, `Secure` com
`COOKIE_SEGURO=true`, `maxAge` = expiração); `src/proxy.ts` redireciona sem sessão válida para
`/login?retorno=...` (no Next 16 o antigo `middleware.ts` passou a se chamar **`proxy.ts`**); `lib/api.ts`
(`server-only`) chama as APIs com o token do cookie; papéis lidos do token no servidor controlam as ações visíveis e
as Server Actions `registrar`/`estornar` recusam sem papel. `Idempotency-Key` gerada ao abrir o formulário, botão
desabilitado no envio. A seção de consolidado tem tratamento de erro isolado: API do consolidado fora ⇒ "Consolidado
temporariamente indisponível" e o extrato continua.

**Alternativas consideradas.** SPA chamando as APIs com token em `localStorage` (exposto a XSS, exige CORS);
NextAuth/Auth.js (mais uma camada sobre um login simples); gateway de API dedicado.

**Por que esta escolha.** Token fora do alcance do JavaScript, sem CORS nas APIs, e a mesma estrutura serve ao
Keycloak depois (troca do login por Authorization Code + PKCE no servidor).

**Consequências.** O servidor Next.js é um salto a mais em toda chamada; sem refresh, a sessão cai em 30 min.

**Onde está no código.** `web/src/proxy.ts`, `web/src/lib/{sessao,token,api,papeis}.ts`, `web/src/app/login/acoes.ts`,
`web/src/app/(autenticado)/contas/[contaId]/{acoes.ts,page.tsx,secao-consolidado.tsx,formulario-lancamento.tsx}`;
testes Vitest ao lado de cada arquivo e `web/e2e/portal.spec.ts` (Playwright + axe, WCAG 2.1 AA, só teclado).

---

## D13. Migrações e seed na inicialização

**Contexto.** O avaliador roda um comando e espera dados prontos; o consolidado precisa de histórico para que a
carga de 50 req/s seja realista.

**Decisão.** Cada API aplica suas migrations EF Core na subida (`Inicializador*`), com retry exponencial até 10 s
enquanto o Postgres não aceita conexões; o worker inicializa o database do consolidado do mesmo jeito. Seed
idempotente: Identidade cria `consulta`, `operador`, `admin`; Lançamentos cria `CC-0001..CC-0005` e ~1 ano de
lançamentos (7.476 na medição) pelo caso de uso **interno** `RegistrarHistoricoLancamentos` — mesmo caminho de domínio
(hash, evento no outbox), instante vindo do item, confirmação em lotes de 1.000 —, com **todo o histórico e a marca
`historico-lancamentos-v1` em `semeaduras` numa única transação**: ou a marca existe e a etapa é pulada, ou nada foi
gravado e é refeito. O consolidado do histórico é populado pelo fluxo de produção (outbox → RabbitMQ → worker).
`SEMEADURA_HISTORICO_HABILITADO=false` desliga o histórico.

**Alternativas consideradas.** *Migration job* separado (container `migrate` que roda e sai); script SQL de seed
inserindo direto nas tabelas do consolidado; seed via API HTTP.

**Por que esta escolha.** Um comando, sem ordem manual. Seed pelo caminho de domínio exercita outbox, broker e
projeção em volume logo na subida — é um teste de fumaça do fluxo inteiro.

**Consequências.** Em produção, migrar na subida é arriscado (várias réplicas competindo, migração lenta segurando o
*ready*, permissões DDL na conta da aplicação) — o certo é *migration job* ([evolução](evolucao.md)). O consolidado
fica completo 1 a 2 minutos após os serviços ficarem `healthy`.

**Divergência.** O design não detalhava como o seed respeitaria o instante de servidor do registro; a implementação
criou o caso de uso interno `RegistrarHistoricoLancamentos` (não exposto por rota) e a transação única com marca.

**Onde está no código.** `src/Banco.Infraestrutura/*/Inicializacao/`; `src/Banco.Aplicacao/Lancamentos/RegistrarHistoricoLancamentos.cs`;
teste `SemeaduraHistoricoTestes.cs`.

---

## D14. Docker Compose e saúde

**Contexto.** Restrição (4) e o requisito de saúde por serviço.

**Decisão.** Serviços `postgres` (17), `redis` (7, AOF), `rabbitmq` (4-management), `identidade-api`,
`lancamentos-api`, `consolidado-api`, `consolidado-worker`, `web`; profile `carga` com `k6` (`grafana/k6:1.3.0`).
`depends_on: condition: service_healthy` e healthcheck em todos; volumes nomeados para dados e chaves; Dockerfiles
multi-stage (`sdk` → `aspnet` não-root; Next `standalone`). Portas: web 3000, identidade 5000, lançamentos 5001,
consolidado 5002, Postgres 5432, Redis 6379, RabbitMQ 5672/15672. `/health/live` (processo) e `/health/ready`
(dependências) em toda API.

- **Healthcheck sem curl:** a imagem `aspnet` não tem `curl`/`wget`; o próprio binário responde a
  `dotnet Banco.Api.X.dll --verificar-saude` fazendo GET em `/health/live` e saindo com 0/1 — sem instalar pacote na
  imagem.
- **Worker sem HTTP:** `PublicadorBatimentoSaude` executa as verificações registradas (`postgres-consolidado`,
  `rabbitmq-consumidor`) a cada 5 s e regrava um arquivo; `--verificar-saude` falha se o batimento tiver mais de 30 s.
  Sem abrir porta só para health.
- **Prontidão deliberadamente assimétrica:** RabbitMQ **fora** do `/health/ready` da `lancamentos-api` (D5) e da
  `consolidado-api` (só usado na DLQ); Redis **dentro** do ready da `consolidado-api` (`503` sinaliza degradação,
  mas as consultas seguem).

**Alternativas consideradas.** Instalar `curl` na imagem; porta HTTP no worker só para health; `depends_on` sem
condição.

**Por que esta escolha.** Imagens mínimas e healthcheck que testa o próprio processo; ordem de subida determinística.

**Consequências.** A primeira subida leva de 5 a 10 minutos (build); o Postgres único com `max_connections` padrão
mostrou o limite na partida a frio (D9).

**Onde está no código.** `docker-compose.yml`, `src/*/Dockerfile`, `web/Dockerfile`,
`src/Banco.Infraestrutura/Compartilhado/Saude/{VerificacaoSaudeLinhaComando,BatimentoSaude,ExtensoesVerificacoesSaude}.cs`;
testes `VerificacoesSaudeTestes.cs`, `BatimentoSaudeTestes.cs`.

---

## D15. Estratégia de testes

**Contexto.** Os riscos estão nas bordas (concorrência, falha de broker, duplicata, ordem), que testes unitários com
mocks não pegam.

**Decisão.** Pirâmide pelo comportamento, no nível mais barato que enxerga o risco:

| Nível | O que cobre | Onde |
|---|---|---|
| Unitário (xUnit, sem Docker) | Domínio (valores, estorno, bloqueio, regras de saldo), emissão de token, padrões de host (auth, rate limit, filtro de log, erros, health), **regras de arquitetura** (NetArchTest) | `tests/Banco.TestesUnitarios/` |
| Integração (WebApplicationFactory do host real + Testcontainers Postgres/RabbitMQ/Redis) | Login/bloqueio, JWKS, idempotência com 20 requisições concorrentes, outbox com broker fora, inbox com duplicata e fora de ordem, DLQ e reprocessamento, 401/403/429, cache com Redis fora | `tests/Banco.TestesIntegracao.{Identidade,Lancamentos,Consolidado}/` |
| Sistema (compose em execução) | Worker e API do consolidado parados, 100 registros, convergência | `tests/Banco.TestesSistema/` (`BANCO_TESTES_SISTEMA=1`) |
| Carga (k6) | 50 req/s por 2 min, normal, com escrita paralela, Redis parado | `tests/carga/consolidado.js`, [nfr.md](nfr.md) |
| Portal (Vitest + Testing Library; Playwright + axe) | Server Actions, proxy, degradação; smoke só com teclado e WCAG 2.1 AA | `web/src/**/*.test.ts(x)`, `web/e2e/portal.spec.ts` |

Autenticação nos testes de integração com tokens assinados por uma chave RSA de teste registrada como chave de
validação — as **mesmas** policies de produção (`tests/Comum/TokensTeste.cs`).

**Alternativas consideradas.** Banco em memória (EF InMemory/SQLite: não reproduz `SKIP LOCKED`, `ON CONFLICT`, UNIQUE
concorrente); mocks de RabbitMQ.

**Por que esta escolha.** As garantias do sistema moram no Postgres e no broker; só testá-las contra os reais prova algo.

**Consequências.** `dotnet test` dos projetos de integração exige Docker; os unitários rodam sem. O teste de sistema é
ignorado por padrão.

**Onde está no código.** `tests/`, `web/src/**/*.test.*`, `web/e2e/`.

---

## D16. Documentação de arquitetura

**Contexto.** A avaliação quer entender como o autor pensa; código sozinho não mostra alternativas descartadas.

**Decisão.** Modelo **C4** em Mermaid (renderizado pelo GitHub) em [`docs/arquitetura/`](arquitetura/README.md):
contexto, containers, componentes por serviço e fluxos de sequência (incluindo os de falha); este documento único;
ADRs curtos em [`docs/adr/`](adr/README.md); [`docs/nfr.md`](nfr.md) com números medidos; [`docs/evolucao.md`](evolucao.md).

**Alternativas consideradas.** Structurizr DSL / PlantUML (exigem ferramenta para renderizar); diagramas como imagem
(desatualizam sem diff); só ADRs.

**Por que esta escolha.** Mermaid versiona como texto, revisa em diff e renderiza no GitHub sem ferramenta.

**Consequências.** O layout automático dos diagramas `C4Container`/`C4Component` do Mermaid sobrepõe rótulos com muitas
relações; por isso cada diagrama C4 de nível 2 e 3 tem também uma visão em flowchart com a notação C4 (cores e
legenda). Todos os diagramas foram validados com `@mermaid-js/mermaid-cli`.

**Onde está no código.** `docs/`.

---

## Consistência eventual como contrato

A separação escrita/leitura (D1) torna o consolidado **eventualmente consistente**: em regime normal fica poucos
segundos atrás dos lançamentos (intervalo do publicador + projeção + TTL de 5 s para consultas que incluem hoje).
Em vez de esconder isso:

- toda resposta do consolidado traz **`atualizadoAte`** (o maior `ocorrido_em` já projetado da conta), e o portal mostra
  "Dados atualizados até";
- nenhum caminho de falha exige intervenção manual para convergir: broker fora ⇒ outbox retém; worker fora ⇒ fila
  quorum retém; erro transitório ⇒ retry; erro permanente ⇒ DLQ + reprocessamento idempotente;
- a fonte de verdade do saldo, para quem precisa de consistência forte, é o razão em Lançamentos (o extrato).

Trade-off aceito: não há leitura *read-your-writes* do consolidado logo após um registro. Comprovação:
`RecuperacaoConsolidadoTestes`, `PublicacaoOutboxTestes`, e na carga a fila terminou com 0 mensagens com 10
lançamentos/s ([nfr.md](nfr.md)).

## Observabilidade

**Implementado.** Logs estruturados JSON (Serilog) com `CorrelationId`: o `MiddlewareCorrelacao` aceita
`X-Correlation-Id` em formato seguro (ou gera um), devolve-o na resposta, grava-o na `mensagens_outbox`, o
publicador o envia como cabeçalho AMQP `x-correlation-id` e o consumidor o coloca no contexto de log da projeção — um
lançamento é seguível ponta a ponta pelos logs. `/health/live` e `/health/ready` com o estado por componente. Problem
Details com o id de correlação.

**Não implementado (non-goal desta entrega).** Métricas (fila, DLQ, idade do outbox pendente, latência) e tracing
distribuído. Caminho em [evolução](evolucao.md#9-observabilidade-opentelemetry-e-jaeger): OpenTelemetry + Jaeger.

## Padrões e práticas aplicados

Resumo dos padrões de projeto e das práticas de arquitetura usados na solução, com o lugar no código e o motivo.
Também aparecem os que foram considerados e não usados, com a razão, porque escolher **não** aplicar um padrão
também é decisão.

### Padrões de projeto

| Padrão | Onde está | Por quê |
|---|---|---|
| **Dependency Injection / Inversion of Control** | Composição de cada host em `Program.cs` e nas extensões `AdicionarInfraestrutura*` (`src/Banco.Infraestrutura/*/Extensoes*.cs`) | A Aplicação depende só de portas (interfaces); a Infraestrutura as implementa e o host decide qual implementação usar. É o que sustenta a regra de dependência da Clean Architecture ([D2](#d2-clean-architecture-organizada-por-camada-com-um-host-por-serviço)) |
| **Unit of Work** | `IUnidadeDeTrabalhoLancamentos`, `IUnidadeDeTrabalhoIdentidade` e a do Consolidado, implementadas sobre o `DbContext` (ex.: `src/Banco.Infraestrutura/Lancamentos/Persistencia/UnidadeDeTrabalhoLancamentos.cs`) | Confirma lançamento + outbox (e inbox + resumo diário) numa única transação ([D2.1](#d21-repository--unit-of-work-só-no-lado-de-escrita)) |
| **Repository** | `RepositorioContas`, `RepositorioLancamentos`, `RepositorioUsuarios` e os repositórios da projeção | Um por agregado, só no lado de escrita; o lado de leitura usa consultas SQL diretas ([D2.1](#d21-repository--unit-of-work-só-no-lado-de-escrita)) |
| **Decorator / Proxy** | `ConsultasConsolidadoEmCache` envolve `ConsultasConsolidado` com a mesma porta `IConsultasConsolidado` (`src/Banco.Infraestrutura/Consolidado/Cache/`); no portal, `web/src/proxy.ts` intercepta as rotas e exige sessão | O cache entra sem que o caso de uso saiba que ele existe, e pode ser desligado trocando o registro de DI ([D9](#d9-cache-e-desempenho-do-consolidado)) |
| **Facade** | `AdicionarPadroesBanco` / `UsarPadroesBanco` (`src/Banco.Infraestrutura/Compartilhado/ExtensoesPadroesBanco.cs`) | Uma chamada configura logs, correlação, Problem Details, autenticação, autorização, rate limit e health checks na ordem certa; os 3 hosts HTTP ficam iguais |
| **Singleton** (via ciclo de vida do contêiner de DI) | `ConexaoRabbitMq`, `IConnectionMultiplexer` do Redis, `CircuitoCacheConsolidado`, `ProcessadorMensagemLancamento` (`src/Banco.Infraestrutura/Consolidado/ExtensoesInfraestruturaConsolidado.cs`) | Conexões caras e estado compartilhado (estado do circuito) precisam de uma instância por processo; o contêiner garante isso sem `static` |
| **Iterator** | `PlanoHistorico` gera o histórico com `yield return` (`src/Banco.Infraestrutura/Lancamentos/Inicializacao/PlanoHistorico.cs`); o extrato é percorrido por cursor (`src/Banco.Aplicacao/Lancamentos/CursorExtrato.cs`) | O seed processa ~7.500 lançamentos em lotes sem montar a lista inteira na memória; o cliente percorre o extrato página a página sem *offset* |
| **Factory** | `Fabrica*DbContextDesign` (`IDesignTimeDbContextFactory`) e as fábricas de teste `FabricaApi*` (`WebApplicationFactory`) | Criam o `DbContext` para as migrations e o host completo para os testes de integração |
| **Mock object / test doubles** | `RelogioFixo` e `RelogioAjustavel` (relógio controlado), `InboxQueFalha` (falha N vezes para provar o retry), `SinkMemoria` (captura logs), `TokensTeste` (emite JWT de teste) | Testam regras dependentes de tempo, falhas transitórias e mascaramento de dados sensíveis de forma determinística |
| **Result** | `ResultadoLancamento` (`src/Banco.Aplicacao/Lancamentos/ResultadoLancamento.cs`) | Conta inexistente, estorno repetido e conflito de idempotência são resultados esperados, não exceções; o host traduz cada um para 404/409/422 |
| **Retry e Circuit Breaker** | Polly no consumidor (`ProcessadorMensagemLancamento`) e no cache (`CircuitoCacheConsolidado`) | Retry com backoff e jitter para falhas transitórias do banco ([D8](#d8-retry-dlq-e-reprocessamento)); circuito para não esperar timeout do Redis a cada requisição ([D9](#d9-cache-e-desempenho-do-consolidado)) |
| **Transactional Outbox / Inbox** | `CaixaSaidaOutbox` + `PublicadorOutbox`; tabela `mensagens_processadas` no Consolidado | Publicação garantida e efeito único no consumidor ([D5](#d5-transactional-outbox), [D7](#d7-projeção-idempotente-e-independente-de-ordem)) |

**Considerados e não usados:**

| Padrão | Por que não |
|---|---|
| **Mediator** (MediatR) | Os casos de uso são chamados diretamente pelos endpoints. O mediator acrescentaria uma indireção sem ganho neste tamanho de solução, e o MediatR passou a ter licença comercial a partir da v13 ([D2](#d2-clean-architecture-organizada-por-camada-com-um-host-por-serviço)) |
| **MVC / MVVM / MVP** | As APIs usam Minimal APIs: cada endpoint só traduz HTTP para um caso de uso, então controllers não acrescentam nada. O portal usa Server Components e Server Actions do Next.js, que separam dados e apresentação sem precisar de um desses padrões |
| **Prototype** | Não é aplicado como padrão. Os *records* do domínio fazem cópia não destrutiva com `with` (ex.: `ResumoDiario`), que cumpre o mesmo papel sem uma interface de clonagem |
| **Composite / Visitor** | Não há hierarquia de objetos a percorrer no domínio atual |

### Estilos e práticas de arquitetura

| Sigla | Aplicação na solução |
|---|---|
| **DDD** | Contextos delimitados (Identidade, Lançamentos, Consolidado) com isolamento verificado por teste de arquitetura; agregados `Conta` e `Lancamento` com regras no domínio; evento de domínio publicado como contrato de integração |
| **EDA** | Lançamentos publica `LancamentoRegistradoV1`; o Consolidado reage de forma assíncrona e idempotente |
| **BFF** | O portal Next.js chama as APIs pelo servidor e guarda o token só em cookie httpOnly ([D12](#d12-portal-nextjs-como-bff)) |
| **CQRS** | Escrita (Lançamentos) e leitura (Consolidado) em processos e bancos separados |
| **HTTP / JSON** | APIs REST versionadas (`/api/v1`) com erros em Problem Details |
| **AMQP** | RabbitMQ com exchange *topic*, filas *quorum*, DLX/DLQ e *publisher confirms* ([D6](#d6-topologia-rabbitmq)) |
| **IaC** | `docker-compose.yml` descreve todo o ambiente de forma declarativa; Terraform e Helm estão na [evolução](evolucao.md#10-infraestrutura-como-código-e-modelo-de-nuvem) |
| **SOA** | Serviços com contratos explícitos e donos de seus dados; nenhum serviço acessa o banco de outro |
| **BDD** | Os comportamentos foram especificados como cenários *Given/When/Then* e guiaram os testes de integração; a automação com Reqnroll está na [evolução](evolucao.md#4-bdd-executável) |
| **gRPC** | Não usado: não há chamada síncrona interna entre serviços. Indicado quando houver ([evolução](evolucao.md#17-integração-com-os-sistemas-do-banco-eventos-e-grpc)) |
| **MQTT** | Não se aplica: é um protocolo para dispositivos e IoT; entre serviços, AMQP oferece as garantias de entrega necessárias |
| **SaaS / PaaS / IaaS** | Escolha do modelo de serviço por componente descrita na [evolução](evolucao.md#10-infraestrutura-como-código-e-modelo-de-nuvem) |

## Divergências entre design e implementação

| Tema | Design | Implementado | Onde |
|---|---|---|---|
| Configuração do JWT nas APIs | `Authority` | `Autenticacao:EnderecoMetadados` (→ `MetadataAddress`), `Emissor`, `Audiencia`, `ExigirHttpsMetadados` | D10 |
| Healthcheck dos containers | Não detalhado | `dotnet <dll> --verificar-saude` (imagem sem curl); worker por arquivo de batimento | D14 |
| Id da mensagem do outbox | Id próprio | Id da mensagem = id do lançamento = `message_id` | D5 |
| Seed do histórico | "Pelo caminho de domínio" | Caso de uso interno `RegistrarHistoricoLancamentos`, histórico + marca numa única transação | D13 |
| Cálculo do consolidado | `CalculadoraSaldo` no domínio | SQL (*window function* + `generate_series`); a calculadora fica como especificação testada | D7 |
| Cache | Cache-aside na API | Decorator de `IConsultasConsolidado` com circuit breaker Polly; saldo sem `em` truncado ao segundo | D9 |
| Proteção de rotas no portal | `middleware.ts` | `proxy.ts` (nome do Next 16) | D12 |
| Isolamento entre contextos | Teste de arquitetura | NetArchTest inspecionando IL, para cada camada × contexto | D2 |
| Prontidão da `lancamentos-api` | Dependências acessíveis | RabbitMQ deliberadamente fora do `/health/ready` | D5, D14 |
| Headers de segurança | HSTS/CSP etc. | Não implementados | D11 |
| p95 < 300 ms | Meta | Cumprida no cenário normal e com Redis parado; instável com escrita paralela e na partida a frio | D9, [nfr.md](nfr.md) |
