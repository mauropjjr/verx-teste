# Requisitos não funcionais

Metas não funcionais da plataforma, como cada uma é verificada e os números medidos no teste de carga do
consolidado .

## Tabela de requisitos

| Requisito | Meta | Como foi verificado | Resultado |
|---|---|---|---|
| Disponibilidade de lançamentos com o consolidado fora | Registrar lançamentos com worker e API do consolidado parados; o saldo converge quando voltam | `tests/Banco.TestesSistema/RecuperacaoConsolidadoTestes.cs` (`Com_worker_e_api_do_consolidado_parados_100_lancamentos_sao_aceitos_e_o_saldo_converge_quando_voltam`, compose em execução, `BANCO_TESTES_SISTEMA=1`); broker fora: `tests/Banco.TestesIntegracao.Lancamentos/PublicacaoOutboxTestes.cs` | Verde quando habilitado (100 de 100 registros `201` e saldo convergido — execução da tarefa 7.2) |
| Desempenho do consolidado | 50 req/s por 2 min em consolidado diário + saldo, ≤ 5% de erro ou perda | k6 `tests/carga/consolidado.js` (`docker compose --profile carga run --rm k6`), sumários em `docs/teste-carga/` | 0,00% de erro e 0 iterações perdidas no cenário normal; ≤ 0,40% de perda em todos os cenários com o sistema aquecido (tabela abaixo) |
| Latência do consolidado (meta de engenharia, D9) | p95 < 300 ms | Mesmo teste (threshold `http_req_duration p(95)<300`) | Cumprida no cenário normal (85,9 ms) e com Redis parado (31,7 ms); **não cumprida de forma estável** com lançamentos em paralelo (276,6 ms e 320,1 ms) e na partida a frio (1.975 ms) — veja a análise |
| Consistência eventual explícita | Toda resposta do consolidado traz `atualizadoAte`; nova versão por conta reflete o dado novo | `tests/Banco.TestesIntegracao.Consolidado/SaldoTestes.cs`, `ConsolidadoDiarioTestes.cs`, `CacheConsolidadoTestes.cs` (`Segunda_consulta_vem_do_cache_e_a_nova_versao_da_conta_reflete_o_dado_novo`); na carga, a fila `consolidado.lancamento-registrado` ficou com 0 mensagens ao fim dos cenários com escrita | Testes verdes na suíte; na carga, o worker acompanhou ~10 lançamentos/s sem acumular fila |
| Idempotência | Mesma `Idempotency-Key` ⇒ mesmo lançamento; corpo diferente ⇒ `422`; concorrência ⇒ um único registro; projeção *at-least-once* neutralizada pelo inbox | `tests/Banco.TestesIntegracao.Lancamentos/IdempotenciaTestes.cs` (20 requisições concorrentes com a mesma chave), `tests/Banco.TestesIntegracao.Consolidado/ConsumidorLancamentosTestes.cs` | Testes verdes na suíte |
| Segurança | JWT RS256 validado (emissor, audiência, assinatura, expiração); papéis consulta/operador/admin; bloqueio após 5 falhas; limite de taxa por `sub` (100 req/s) e login por IP; logs sem `Authorization`/senha | `tests/Banco.TestesUnitarios/Compartilhado/AutenticacaoAutorizacaoTestes.cs`, `LimiteTaxaTestes.cs`, `FiltroDadosSensiveisTestes.cs`, `tests/Banco.TestesIntegracao.Identidade/LoginTestes.cs` | Testes verdes na suíte; a carga de 50 req/s com um único usuário (`consulta`) não recebeu nenhum `429` |
| Observabilidade | Logs JSON estruturados com `traceId`/`X-Correlation-Id`; `/health/live` e `/health/ready` (ready `503` sem Redis no consolidado); erros em Problem Details sem detalhes internos | `tests/Banco.TestesUnitarios/Compartilhado/TratamentoErrosECorrelacaoTestes.cs`, `VerificacoesSaudeTestes.cs`, `tests/Banco.TestesIntegracao.Consolidado/CacheConsolidadoTestes.cs` (`..._ready_responde_503`) | Testes verdes na suíte; com o Redis parado (cenário 3), `GET http://consolidado-api:8080/health/ready` respondeu `503` (`redis: Unhealthy`) e as 6.001 consultas da carga responderam `200` |
| Build sem warnings | `dotnet build -warnaserror` com 0 avisos | `Directory.Build.props` (`TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild`); `dotnet build Banco.slnx -warnaserror` | 0 avisos, 0 erros |

## Teste de carga do consolidado

### Ambiente da medição

- **Data:** 2026-10-13.
- **Máquina:** notebook Windows 10 Pro, Intel Core i7-6500U (2 núcleos / 4 threads, 2,5 GHz), 15,7 GB de RAM.
- **Docker Desktop** 29.5.3 (VM linuxkit com 4 vCPU e 5,8 GB); outros containers não relacionados ao projeto
  rodando na mesma máquina durante a medição (dois Postgres e duas APIs de outros projetos, ociosos).
- **Tudo na mesma VM:** k6 (`grafana/k6:1.3.0`), Postgres 17, Redis 7, RabbitMQ 4, as três APIs, o worker e o
  portal. O gerador de carga disputa CPU com o sistema medido — os números são conservadores para o hardware.
- **Dados:** seed de 1 ano (7.476 lançamentos em CC-0001..CC-0005, todos projetados antes da primeira rodada)
  mais os lançamentos criados pelas rodadas anteriores.
- **Carga:** `constant-arrival-rate` 50 req/s por 2 min (6.000 iterações esperadas), token do usuário `consulta`
  obtido no `setup()`. Mistura por iteração: consolidado diário dos últimos 30 dias (20%), 90 dias (15%),
  365 dias (10%), um mês fechado do último ano (20%); saldo atual (15%) e saldo em um instante passado (20%),
  conta sorteada entre as 5.

### Resultados (sumários em `docs/teste-carga/`)

Métricas das **consultas** (`scenario:consultas`). "Perda" = (requisições com erro + `dropped_iterations`) / 6.000.

| Cenário (arquivo) | Throughput | Mediana | p95 | p99 | Máx. | Erro | `dropped_iterations` | Perda | Thresholds |
|---|---|---|---|---|---|---|---|---|---|
| 1. Normal (`01-consolidado-normal`) | 49,58 req/s (6.001) | 7,1 ms | **85,9 ms** | 305,3 ms | 1.230,4 ms | 0,00% | 0 | 0,00% | todos OK |
| 2a. Com lançamentos em paralelo (`02-consolidado-com-lancamentos`) | 49,25 req/s (6.001) | 35,8 ms | **276,6 ms** | 466,3 ms | 876,6 ms | 0,00% | 2 | 0,03% | todos OK |
| 2b. Idem, repetição (`02-consolidado-com-lancamentos-repeticao`) | 49,12 req/s (6.001) | 40,1 ms | **320,1 ms** | 593,9 ms | 1.928,7 ms | 0,00% | 24 | 0,40% | **p95 falhou** |
| 3. Redis parado (`03-consolidado-redis-parado`) | 49,76 req/s (6.001) | 5,2 ms | **31,7 ms** | 64,4 ms | 216,1 ms | 0,00% | 0 | 0,00% | todos OK |
| 4. Partida a frio (`04-consolidado-partida-a-frio`) | 48,73 req/s (5.863) | 6,4 ms | **1.975,2 ms** | 6.093,3 ms | 7.961,6 ms | 0,20% (12) | 138 | 2,50% | **p95 falhou** |

Lançamentos registrados em paralelo (cenário `lancamentos`, operador, 10 req/s, `Idempotency-Key` único):

| Cenário | Registros | Throughput | p95 | p99 | Erro |
|---|---|---|---|---|---|
| 2a | 1.198 | 9,83 req/s | 355,3 ms | 770,4 ms | 0,00% |
| 2b | 1.177 | 9,63 req/s | 541,8 ms | 4.236,9 ms | 0,00% |

Como cada cenário foi produzido (comandos no README):

1. **Normal:** ambiente no ar, seed já projetado, sistema aquecido por uma rodada curta anterior.
2. **Com lançamentos em paralelo:** `K6_LANCAMENTOS_PARALELOS=1` — 10 POSTs/s em `lancamentos-api`; o worker
   projetou tudo durante a carga (fila principal e DLQ com 0 mensagens ao final) e cada projeção incrementou a
   versão da conta no Redis, invalidando o cache.
3. **Redis parado:** `docker compose stop redis` e `run --rm --no-deps k6` (sem `--no-deps` o compose religaria o
   Redis, que é dependência da API). Rodou com o **mesmo** limite p95 de 300 ms (`K6_LIMITE_P95_MS` não foi
   alterado).
4. **Partida a frio:** `docker compose restart consolidado-api` e a carga começando logo que o container ficou
   healthy (Redis e Postgres já aquecidos).

Execuções que não ficaram em arquivo (o sumário foi sobrescrito pela rodada seguinte de mesmo nome, depois de
o script ganhar o detalhamento por tipo de consulta), registradas aqui pela saída do k6 na mesma sessão:

- Primeira rodada do cenário 2: p95 416,9 ms, p99 1.259,3 ms, 0,00% de erro, 28 `dropped_iterations` — **p95 falhou**.
- Primeira rodada do cenário 1: p95 53,1 ms, p99 119,6 ms, 0,00% de erro, 0 `dropped_iterations`.
- Primeira rodada curta (20 s) logo após a subida do ambiente: p95 7.919,6 ms, 1,57% de erro, 174 de 1.000
  iterações descartadas (perda de 18,7%) — mesmo fenômeno do cenário 4, com o ambiente inteiro recém-iniciado.

Uso do Redis (delta de `keyspace_hits`/`keyspace_misses` do `INFO stats` antes e depois da rodada; cada consulta
faz um `GET` da versão da conta e um `GET` do resultado):

| Cenário | Hits | Misses | Acerto estimado do resultado¹ |
|---|---|---|---|
| 1. Normal | 10.283 | 1.719 | ~71% |
| 2a. Com lançamentos | 7.805 | 4.197 | ~30% |

¹ Supondo o `GET` da versão sempre com acerto: (hits − 6.001) / 6.001.

### Análise

- **Meta da especificação atendida:** em todos os cenários com o sistema aquecido a perda ficou entre 0,00% e 0,40% (meta
  ≤ 5%), sem nenhuma resposta de erro e sem `429` — o limite de 100 req/s por `sub` comporta os 50 req/s de um
  único usuário, então a carga não precisou ser distribuída entre usuários. Mesmo na partida a frio a perda foi
  2,50%, ainda dentro da meta.
- **p95 < 300 ms (D9) não é estável com escrita concorrente:** com 10 lançamentos/s em paralelo o p95 das
  consultas variou de 276,6 ms a 416,9 ms em três rodadas (uma passou, duas falharam). Toda a plataforma divide
  4 vCPU num notebook de 2 núcleos físicos: a API de lançamentos (transação + outbox), o publicador, o RabbitMQ,
  o worker (inbox + projeção + `INCR` da versão) e o Postgres competem com a API do consolidado e com o próprio
  k6. O cenário 3 mostra que o Postgres sozinho responde o consolidado em p95 de 31,7 ms, então o aumento vem da
  contenção de CPU/E-S causada pela escrita, não do custo da consulta.
- **Efeito do cache:** a invalidação por versão funciona como projetado (o acerto cai de ~71% para ~30% quando as
  contas recebem ~2 lançamentos/s cada, pois **qualquer** projeção invalida também os períodos passados da conta).
  Com o volume atual (1 ano × 5 contas ≈ 1,8 mil resumos diários) o Postgres responde a consulta com *window
  function* em poucos milissegundos com o buffer quente, e o caminho com Redis (dois `GET`, serialização e `SET`
  nos misses) chega a ser mais lento que ir direto ao banco (p95 85,9 ms com Redis × 31,7 ms sem). O cache
  protege o Postgres conforme o histórico cresce, mas, nesta escala, não é ele que garante a latência. Pela
  mesma razão, o cenário degradado (Redis parado) cumpriu a meta com folga e o circuito aberto do Polly evitou
  esperas no Redis (o `/health/ready` respondeu `503`, como especificado).
- **Gargalo na partida a frio:** nos primeiros ~15 s após o container subir, as requisições se acumulam
  (JIT, *thread pool* ainda pequeno — os logs mostram `QueuedItems` alto e *timeout* ao conectar no Redis, que
  abre o circuito) e cada requisição represada abre uma conexão nova: o Postgres recusou conexões com
  `53300: sorry, too many clients already` (12 respostas `500`). Passada a rampa, a latência volta ao normal
  (mediana 6,4 ms na mesma rodada). Mitigações candidatas, fora do escopo desta change: limitar `Maximum Pool
  Size` do Npgsql abaixo de `max_connections` somado entre os serviços (ou PgBouncer), aquecer a API antes de
  ela ficar *ready* e elevar o mínimo do *thread pool*.
- **Reprodutibilidade:** os números variam entre rodadas (veja 2a × 2b) por causa do ambiente compartilhado;
  rode cada cenário mais de uma vez antes de comparar mudanças de desempenho.
