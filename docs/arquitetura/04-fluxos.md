# Fluxos principais e de falha (diagramas de sequência)

Visão dinâmica do C4: como os containers e componentes dos níveis [2](02-containers.md) e
[3](03-componentes-lancamentos.md) colaboram no tempo. Os nomes são os das classes, tabelas, filas e rotas reais.
Cada fluxo indica o teste que o comprova.

**Legenda:** seta cheia (`->>`) = chamada síncrona; seta tracejada (`-->>`) = resposta; `alt` = caminhos
alternativos; `par` = execuções concorrentes; `loop` = repetição; nota amarela = regra ou detalhe relevante.

Sumário:
[1. Login](#1-login) ·
[2. Registro com idempotência](#2-registro-de-lançamento-com-idempotência) ·
[3. Outbox → RabbitMQ](#3-outbox--rabbitmq) ·
[4. Projeção no worker](#4-projeção-no-worker-inbox--upsert--versão) ·
[5. Consulta com cache](#5-consulta-do-consolidado-com-cache) ·
[6. Consolidado fora do ar](#6-consolidado-fora-do-ar-e-recuperação) ·
[7. DLQ e reprocessamento](#7-dlq-e-reprocessamento)

## 1. Login

```mermaid
sequenceDiagram
  autonumber
  actor U as Usuário (navegador)
  participant W as web (Server Action entrar)
  participant I as identidade-api
  participant A as AutenticarUsuario
  participant DB as database identidade
  participant API as lancamentos-api / consolidado-api

  U->>W: POST /login (login, senha, retorno)
  W->>I: POST /api/v1/autenticacao/login
  Note over I: RateLimiter login-por-ip (30/min) → 429 + Retry-After
  I->>A: ExecutarAsync(ComandoAutenticar)
  A->>DB: RepositorioUsuarios.ObterPorLoginAsync
  alt usuário existe
    A->>A: VerificadorSenha.Verificar (PBKDF2)
    A->>A: Usuario.RegistrarTentativaLogin (5 falhas ⇒ bloqueio 15 min)
    A->>DB: UnidadeDeTrabalhoIdentidade.ConfirmarAsync (tentativas)
  end
  alt aceito
    A->>A: EmissorTokenJwt.Emitir (RS256, kid, 30 min)
    I-->>W: 200 { tokenAcesso, expiraEm }
    W->>W: cookie "sessao" httpOnly, SameSite=Lax, maxAge = expiração
    W-->>U: redirect para retorno (ex.: /contas)
  else inexistente, senha errada ou bloqueado
    I-->>W: 401 (mesma resposta nos três casos)
    W-->>U: "Usuário ou senha inválidos"
  end

  Note over U,API: Requisições seguintes
  U->>W: GET /contas (cookie sessao)
  W->>W: proxy.ts: token ausente/expirado ⇒ redirect /login?retorno=...
  W->>API: GET /api/v1/contas (Authorization: Bearer)
  opt primeira validação ou kid desconhecido
    API->>I: GET /.well-known/openid-configuration e /.well-known/jwks.json
  end
  API->>API: JwtBearer valida emissor, audiência, assinatura, expiração e papel
  API-->>W: 200
  W-->>U: página renderizada no servidor (o token não vai ao JavaScript)
```

Testes: `tests/Banco.TestesIntegracao.Identidade/LoginTestes.cs`, `ChavePublicaTestes.cs`;
`web/src/app/login/acoes.test.ts`, `web/src/proxy.test.ts`.

## 2. Registro de lançamento com idempotência

```mermaid
sequenceDiagram
  autonumber
  participant W as web (Server Action registrar)
  participant E as EndpointsLancamentos
  participant R as RegistrarLancamento
  participant UoW as UnidadeDeTrabalhoLancamentos
  participant DB as database lancamentos

  Note over W: Idempotency-Key gerada ao abrir o formulário (campo oculto), reenviada nas retentativas
  W->>E: POST /api/v1/contas/CC-0001/lancamentos + Idempotency-Key
  E->>E: FluentValidation (tipo, valor > 0 com 2 casas, descrição, chave) → 400
  E->>R: ExecutarAsync(ComandoRegistrarLancamento)
  R->>R: hash = SHA-256 do corpo normalizado
  R->>DB: RepositorioContas.ObterAsync → 404 se não existe
  R->>DB: RepositorioLancamentos.ObterPorChaveIdempotenciaAsync
  alt reenvio: chave já usada nesta conta
    alt mesmo hash
      R-->>E: Repetido (lançamento original)
      E-->>W: 201 com o MESMO id
    else hash diferente
      R-->>E: ChaveReutilizadaComOutroConteudo
      E-->>W: 422
    end
  else chave nova
    R->>R: Lancamento.Registrar (ocorridoEm = relógio do servidor)
    R->>R: CaixaSaidaOutbox.Adicionar(LancamentoRegistradoV1)
    R->>UoW: ConfirmarAsync
    UoW->>DB: BEGIN · INSERT lancamentos · INSERT mensagens_outbox · COMMIT
    UoW-->>R: Confirmado
    E-->>W: 201 { id, contaId, tipo, valor, descricao, ocorridoEm }
  end
```

**Corrida** — duas requisições simultâneas com a mesma chave passam juntas pela verificação de reenvio; quem decide
é a UNIQUE do banco, sem lock distribuído:

```mermaid
sequenceDiagram
  autonumber
  participant A as Requisição A
  participant B as Requisição B
  participant DB as database lancamentos

  par
    A->>DB: ObterPorChaveIdempotenciaAsync → nenhum
  and
    B->>DB: ObterPorChaveIdempotenciaAsync → nenhum
  end
  A->>DB: INSERT lancamentos + mensagens_outbox → COMMIT
  B->>DB: INSERT lancamentos → 23505 em ux_lancamentos_conta_chave_idempotencia
  Note over B: UoW devolve ChaveIdempotenciaDuplicada (nada gravado, ChangeTracker limpo)
  B->>DB: Idempotencia.ResultadoDoVencedorAsync → lê o lançamento de A
  Note over A,B: As duas respondem 201 com o mesmo id (ou B responde 422 se o corpo era diferente)
```

O estorno (`POST .../lancamentos/{id}/estorno`) segue o mesmo desenho; a UNIQUE `ux_lancamentos_estorno_de` faz o
segundo estorno concorrente virar `409`.

Testes: `tests/Banco.TestesIntegracao.Lancamentos/IdempotenciaTestes.cs` (20 requisições concorrentes com a mesma
chave), `RegistroLancamentoTestes.cs`, `EstornoTestes.cs`, `OutboxTestes.cs`.

## 3. Outbox → RabbitMQ

```mermaid
sequenceDiagram
  autonumber
  participant P as PublicadorOutbox (lancamentos-api)
  participant DB as database lancamentos
  participant MQ as RabbitMQ

  loop a cada 1 s (ou de imediato se o lote veio cheio)
    P->>MQ: canal com publisher confirms (reaproveitado se aberto)
    P->>DB: BEGIN · SELECT * FROM mensagens_outbox WHERE publicado_em IS NULL ORDER BY ocorrido_em LIMIT 100 FOR UPDATE SKIP LOCKED
    loop cada mensagem do lote
      P->>MQ: basic.publish lancamentos.eventos / lancamento.registrado.v1 (mandatory, persistente, message_id = id do lançamento, x-correlation-id)
      MQ-->>P: ack (roteada para consolidado.lancamento-registrado)
      P->>P: publicado_em = agora
    end
    P->>DB: UPDATE + COMMIT
  end

  alt broker fora ao abrir o canal
    P--xMQ: falha de conexão
    P->>P: descarta canal · backoff 1 s, 2 s, 4 s ... até 30 s
    Note over P,DB: Os registros continuam respondendo 201: as mensagens ficam pendentes no Postgres
  else nack, retorno (sem fila) ou queda no meio do lote
    P->>P: tentativas++ na mensagem que falhou, interrompe o lote
    P->>DB: COMMIT (grava as já confirmadas e o contador)
    P->>P: backoff e nova tentativa
  end
  Note over P,MQ: Queda entre o ack do broker e o COMMIT ⇒ a mensagem é publicada de novo (at-least-once), o inbox do Consolidado descarta a duplicata
```

Por que o RabbitMQ **não** está no `/health/ready` da `lancamentos-api`: tirar a API do ar por causa do broker
derrubaria exatamente o que o outbox existe para proteger.

Testes: `tests/Banco.TestesIntegracao.Lancamentos/PublicacaoOutboxTestes.cs` (broker fora no registro, publicação
depois da volta).

## 4. Projeção no worker (inbox + UPSERT + versão)

```mermaid
sequenceDiagram
  autonumber
  participant MQ as RabbitMQ
  participant C as ConsumidorLancamentosRegistrados
  participant PR as ProcessadorMensagemLancamento
  participant PJ as ProjetarLancamento
  participant DB as database consolidado
  participant R as Redis

  MQ->>C: deliver (prefetch 20, manual ack)
  C->>C: log com CorrelationId (x-correlation-id) e IdMensagem
  C->>PR: ProcessarAsync(corpo)
  PR->>PR: desserializa LancamentoRegistradoV1 (inválido ⇒ Rejeitar)
  PR->>PJ: ExecutarAsync (escopo DI novo, dentro do retry Polly)
  PJ->>DB: BEGIN
  PJ->>DB: INSERT INTO mensagens_processadas (id_mensagem) ON CONFLICT DO NOTHING
  alt 1 linha inserida (primeira vez)
    PJ->>DB: INSERT resumos_diarios ... ON CONFLICT (conta_id, dia) DO UPDATE creditos = creditos + x, debitos = debitos + y
    PJ->>DB: INSERT lancamentos_projetados (saldo intradiário)
    PJ->>DB: UPSERT marcas_atualizacao (ultimo_ocorrido_em = GREATEST(...))
    PJ->>DB: COMMIT
  else 0 linhas (duplicata)
    PJ->>DB: ROLLBACK (nada muda)
  end
  PJ->>R: INCR versao:{conta} (também na duplicata, Redis fora ⇒ só log)
  PJ-->>PR: Projetado | Duplicado
  PR-->>C: Confirmar
  C->>MQ: basic.ack
  Note over PJ,DB: Só somas comutativas e um máximo ⇒ eventos fora de ordem produzem o mesmo resultado
```

Testes: `tests/Banco.TestesIntegracao.Consolidado/ProjecaoLancamentoTestes.cs` (duplicata, fora de ordem, estorno),
`ConsumidorLancamentosTestes.cs` (versão no Redis, correlação no log, falha transitória retentada).

## 5. Consulta do consolidado com cache

```mermaid
sequenceDiagram
  autonumber
  participant W as web
  participant E as EndpointsConsolidado
  participant CS as ConsultarSaldo / ConsultarConsolidadoDiario
  participant C as ConsultasConsolidadoEmCache
  participant CB as CircuitoCacheConsolidado
  participant R as Redis
  participant Q as ConsultasConsolidado (SQL)
  participant DB as database consolidado

  W->>E: GET /api/v1/contas/{contaId}/saldo?em= (ou consolidado-diario?de=&ate=)
  E->>CS: ExecutarAsync (sem em ⇒ agora truncado ao segundo, período ≤ 366 dias)
  CS->>C: IConsultasConsolidado
  C->>CB: TentarAsync
  alt circuito fechado e Redis no ar
    CB->>R: GET versao:{conta} → v
    CB->>R: GET saldo:{conta}:{v}:{em}
    alt hit
      C-->>E: resultado do cache
    else miss
      C->>Q: ConsultarSaldoAsync
      Q->>DB: 1 SELECT (deltas anteriores + lancamentos_projetados do dia + marca)
      C->>R: SET chave valor EX 5 s (inclui hoje) ou 600 s (só dias passados)
      C-->>E: resultado do banco
    end
  else Redis fora (timeout 500 ms, FailFast) ou circuito aberto
    CB-->>C: null (circuito abre com 50% de falhas, mínimo 2 em 10 s, por 15 s)
    C->>Q: consulta direto no Postgres, sem SET
    Note over E: /health/ready responde 503 (componente redis), mas as consultas seguem 200
  end
  E-->>W: 200 { saldo, em, atualizadoAte }
```

Testes: `tests/Banco.TestesIntegracao.Consolidado/CacheConsolidadoTestes.cs` (hit, nova versão, Redis fora,
`/health/ready` 503); carga com Redis parado em [nfr.md](../nfr.md).

## 6. Consolidado fora do ar e recuperação

```mermaid
sequenceDiagram
  autonumber
  actor O as Operador
  participant W as web
  participant L as lancamentos-api
  participant MQ as RabbitMQ (fila quorum)
  participant WK as consolidado-worker
  participant CA as consolidado-api

  Note over WK,CA: docker compose stop consolidado-worker consolidado-api
  O->>W: registra 100 lançamentos
  W->>L: POST .../lancamentos (×100)
  L-->>W: 201 (×100): lançamento + outbox não dependem do Consolidado
  L->>MQ: PublicadorOutbox publica, mensagens acumulam em consolidado.lancamento-registrado
  W->>CA: GET .../saldo
  CA--xW: falha de conexão
  W-->>O: extrato normal + seção "Consolidado temporariamente indisponível"
  Note over WK,CA: docker compose start consolidado-worker consolidado-api
  MQ->>WK: entrega o acumulado (prefetch 20)
  WK->>WK: projeção idempotente (fluxo 4) + INCR versao
  O->>W: abre a conta de novo
  W->>CA: GET .../saldo
  CA-->>W: saldo convergido, atualizadoAte avançado
```

O mesmo vale para o broker fora (fluxo 3): os eventos ficam no outbox em vez de na fila. Nenhum caminho exige
intervenção manual para convergir.

Testes: `tests/Banco.TestesSistema/RecuperacaoConsolidadoTestes.cs` (compose em execução, `BANCO_TESTES_SISTEMA=1`;
100 de 100 registros `201` e saldo convergido); `web/src/app/(autenticado)/contas/[contaId]/page.test.tsx`
(degradação da seção).

## 7. DLQ e reprocessamento

```mermaid
sequenceDiagram
  autonumber
  participant MQ as RabbitMQ
  participant C as ConsumidorLancamentosRegistrados
  participant PR as ProcessadorMensagemLancamento
  participant DLQ as consolidado.lancamento-registrado.dlq
  actor A as Admin
  participant API as consolidado-api (EndpointsAdministracao)
  participant F as FilaMensagensMortasRabbitMq

  MQ->>C: deliver
  C->>PR: ProcessarAsync
  alt JSON inválido ou EventoInvalidoException (não transitória)
    PR-->>C: Rejeitar (sem nova tentativa)
  else falha transitória (NpgsqlException.IsTransient, timeout)
    loop 3 novas tentativas, backoff exponencial com jitter (~1 s, 2 s, 4 s)
      PR->>PR: ProjetarLancamento em escopo novo
    end
    PR-->>C: Rejeitar (esgotou)
  end
  C->>MQ: basic.nack(requeue: false)
  MQ->>DLQ: via DLX consolidado.dlx (fanout), as mensagens seguintes não ficam bloqueadas

  Note over A,API: Causa corrigida (ex.: banco de volta)
  A->>API: POST /api/v1/admin/dlq/reprocessar?maximo=100 (papel admin, operador ⇒ 403)
  API->>F: ReprocessarDlq.ExecutarAsync (1 ≤ maximo ≤ 1000)
  loop até maximo ou DLQ vazia
    F->>DLQ: basic.get (sem auto-ack)
    F->>MQ: publish em lancamentos.eventos com routing key e propriedades originais (message_id, x-correlation-id), aguarda confirm
    alt confirmada
      F->>DLQ: ack (sai da DLQ)
    else falhou
      F->>DLQ: nack(requeue: true) (volta para a DLQ)
    end
  end
  API-->>A: 200 { reenviadas }
  MQ->>C: entrega de novo → projeção, o inbox descarta o que já tinha sido projetado
```

Testes: `tests/Banco.TestesIntegracao.Consolidado/ReprocessamentoDlqTestes.cs`, `ConsumidorLancamentosTestes.cs`
(mensagem envenenada não bloqueia as seguintes).
