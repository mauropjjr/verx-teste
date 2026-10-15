# Banco — Lançamentos e Saldo Consolidado

Plataforma de controle de fluxo de caixa: um serviço de **lançamentos** (débitos e créditos, fonte de verdade) e
um serviço de **consolidado diário** (saldo por dia e saldo em um instante), separados para que o registro de
lançamentos continue disponível mesmo com o consolidado fora do ar. A comunicação entre os dois é assíncrona
(Transactional Outbox → RabbitMQ → worker com inbox idempotente), e a leitura do consolidado é cacheada no Redis.

Stack: .NET 10 (Clean Architecture, um host por serviço), PostgreSQL 17, Redis 7, RabbitMQ 4, Next.js (portal),
Docker Compose. Autenticação própria com JWT RS256 (serviço de identidade + JWKS).

## Pré-requisitos

- **Docker Desktop** (ou Docker Engine) com **Compose v2** — é tudo o que é preciso para subir e usar o ambiente.
- Só para rodar os testes fora do Docker: **.NET 10 SDK** (unitários, integração e sistema) e **Node.js 24**
  (portal: lint, Vitest e Playwright).

## Subir o ambiente

```bash
docker compose up --build
# ou em segundo plano:
docker compose up --build -d
docker compose ps   # todos os serviços devem ficar "healthy"
```

- **Primeira subida:** de 5 a 10 minutos, dependendo da rede e da máquina (download das imagens base, `restore`
  do NuGet e build das imagens). As seguintes levam cerca de 1 minuto.
- Na primeira subida, as APIs aplicam as migrações e semeiam os dados de demonstração: 3 usuários, 5 contas
  (`CC-0001` a `CC-0005`) e **~1 ano de lançamentos** (cerca de 7.500, em ~10 s). O histórico passa pelo mesmo
  fluxo de produção (outbox → RabbitMQ → worker), então o consolidado fica completo **1 a 2 minutos** depois
  que os serviços ficam `healthy`. O seed é idempotente: reiniciar o ambiente não duplica dados. Para subir sem
  o histórico, use `SEMEADURA_HISTORICO_HABILITADO=false` (veja `.env.example`).
- Credenciais e portas têm defaults de desenvolvimento; para mudar, copie `.env.example` para `.env` e ajuste.

## Serviços e portas

| Serviço | URL / porta no host | Observação |
|---|---|---|
| Portal web (`web`) | http://localhost:3000 | Portal Next.js (BFF): login, contas, extrato e consolidado |
| Identidade (`identidade-api`) | http://localhost:5000 | Login (`POST /api/v1/autenticacao/login`) e JWKS (`/.well-known/jwks.json`) |
| Lançamentos (`lancamentos-api`) | http://localhost:5001 | Contas, lançamentos, estorno e extrato |
| Consolidado (`consolidado-api`) | http://localhost:5002 | Consolidado diário, saldo e reprocessamento da DLQ |
| Worker do consolidado (`consolidado-worker`) | — (sem porta) | Consome os eventos e projeta o consolidado |
| RabbitMQ management | http://localhost:15672 | AMQP em `localhost:5672` |
| PostgreSQL | `localhost:5432` | Databases `identidade`, `lancamentos` e `consolidado` |
| Redis | `localhost:6379` | Cache do consolidado |

Toda API expõe `/health/live` e `/health/ready` sem autenticação.

## Portal web

Abra http://localhost:3000 e entre com um dos usuários abaixo (ex.: `operador` / `Operador@123`). Sem sessão,
qualquer página redireciona para `/login` e, depois do login, volta à página pedida. O portal é um BFF: o
servidor Next.js chama as APIs com o JWT guardado em cookie **httpOnly** e `SameSite=Lax` (com `Secure` quando
`COOKIE_SEGURO=true`, para uso atrás de HTTPS), então o token nunca chega ao JavaScript do navegador. Páginas:
`/contas` (titular mascarado), `/contas/<id>` (extrato paginado, saldo do consolidado e — para `operador`/`admin`
— registro de crédito/débito e estorno com confirmação; se a API de consolidado cair, só essa seção mostra
"Consolidado temporariamente indisponível") e `/contas/<id>/consolidado` (consolidado diário de um período, padrão
últimos 30 dias e até 366, com "Dados atualizados até", e saldo em um instante escolhido no horário de Brasília).
O formulário de lançamento gera a `Idempotency-Key` ao abrir e a reenvia nas retentativas; o botão fica
desabilitado durante o envio, então um duplo clique cria um único lançamento. Quem tem só o papel `consulta` não vê
as ações de escrita (e as Server Actions recusam no servidor).

Para rodar o portal fora do Docker (com as APIs no ar pelo compose): `npm --prefix web ci` e
`npm --prefix web run dev`; as URLs das APIs vêm de `IDENTIDADE_URL`, `LANCAMENTOS_URL` e `CONSOLIDADO_URL`
(padrões `http://localhost:5000`, `5001` e `5002`).

## Usuários e senhas (somente ambiente local)

| Onde | Usuário | Senha | Papel / permissões |
|---|---|---|---|
| Identidade / portal | `consulta` | `Consulta@123` | `consulta`: contas, extrato, consolidado e saldo |
| Identidade / portal | `operador` | `Operador@123` | `operador`: o que `consulta` faz + registrar e estornar lançamentos |
| Identidade / portal | `admin` | `Admin@123` | `admin`: o que `operador` faz + reprocessar a DLQ |
| RabbitMQ management | `banco` | `banco_dev` | Administrador do broker |
| PostgreSQL | `banco` | `banco_dev` | Dono dos três databases |
| Redis | — | — | Sem senha (só rede local) |

Os valores vêm do `docker-compose.yml` e podem ser trocados pelo `.env` (`POSTGRES_*`, `RABBITMQ_*`,
`IDENTIDADE_SENHA_*`). O token JWT expira em 30 minutos; não há refresh token — refaça o login.

## Exemplos com curl

Em Bash (Git Bash, WSL, Linux, macOS). No PowerShell, use `curl.exe` e aspas adequadas, ou `Invoke-RestMethod`.

```bash
# 1) Login (o token vem em "tokenAcesso")
TOKEN=$(curl -s -X POST http://localhost:5000/api/v1/autenticacao/login \
  -H "Content-Type: application/json" \
  -d '{"login":"operador","senha":"Operador@123"}' | sed -E 's/.*"tokenAcesso":"([^"]+)".*/\1/')

# 2) Contas
curl -s http://localhost:5001/api/v1/contas -H "Authorization: Bearer $TOKEN"

# 3) Registrar lançamento (Idempotency-Key obrigatório: reenviar a mesma chave não duplica)
curl -s -X POST http://localhost:5001/api/v1/contas/CC-0001/lancamentos \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: exemplo-0001" \
  -d '{"tipo":"CREDITO","valor":150.75,"descricao":"Deposito em dinheiro"}'

# 4) Estornar um lançamento (id devolvido no passo 3)
curl -s -X POST http://localhost:5001/api/v1/contas/CC-0001/lancamentos/<id>/estorno \
  -H "Authorization: Bearer $TOKEN" -H "Idempotency-Key: exemplo-estorno-0001"

# 5) Extrato paginado (mais recentes primeiro; siga "proximoCursor" com &cursor=...)
curl -s "http://localhost:5001/api/v1/contas/CC-0001/lancamentos?limite=20" -H "Authorization: Bearer $TOKEN"

# 6) Consolidado diário de um período (até 366 dias)
curl -s "http://localhost:5002/api/v1/contas/CC-0001/consolidado-diario?de=2026-01-01&ate=2026-01-31" \
  -H "Authorization: Bearer $TOKEN"

# 7) Saldo em um instante (sem "em", usa agora)
curl -s "http://localhost:5002/api/v1/contas/CC-0001/saldo" -H "Authorization: Bearer $TOKEN"

# 8) Reprocessar a DLQ (somente admin)
ADMIN=$(curl -s -X POST http://localhost:5000/api/v1/autenticacao/login \
  -H "Content-Type: application/json" \
  -d '{"login":"admin","senha":"Admin@123"}' | sed -E 's/.*"tokenAcesso":"([^"]+)".*/\1/')
curl -s -X POST "http://localhost:5002/api/v1/admin/dlq/reprocessar?maximo=100" -H "Authorization: Bearer $ADMIN"
```

As respostas do consolidado trazem `atualizadoAte`: o consolidado é eventualmente consistente e fica, em regime
normal, poucos segundos atrás dos lançamentos.

## Testes

```bash
# Tudo o que é .NET (unitários + integração; o teste de sistema aparece como "Skipped")
dotnet test Banco.slnx

# Build sem warnings e formatação
dotnet build Banco.slnx -warnaserror
dotnet format Banco.slnx --verify-no-changes
```

| Nível | Comando | Requisitos |
|---|---|---|
| Unitários (domínio, aplicação, padrões de host, arquitetura) | `dotnet test tests/Banco.TestesUnitarios` | .NET 10 SDK; **sem Docker** |
| Integração (APIs reais + Postgres/RabbitMQ/Redis via Testcontainers) | `dotnet test tests/Banco.TestesIntegracao.Identidade` · `...Lancamentos` · `...Consolidado` | .NET 10 SDK + Docker em execução |
| Sistema (compose em execução) | veja abaixo | Ambiente no ar (`docker compose up --build -d`) |
| Carga (k6, consolidado a 50 req/s) | `docker compose --profile carga run --rm k6` | Ambiente no ar (`docker compose up --build -d`); veja abaixo |
| Portal: lint, unitários/componentes (Vitest + Testing Library) e build | `npm --prefix web ci`, depois `npm --prefix web run lint` · `npm --prefix web run test` · `npm --prefix web run build` | Node.js 24; **sem Docker** |
| E2E do portal (Playwright + axe) | `npm --prefix web run test:e2e` | Node.js 24 + ambiente no ar (`docker compose up --build -d`) + `npx --prefix web playwright install chromium` (uma vez) |

### E2E do portal (smoke)

`web/e2e/portal.spec.ts` roda contra o portal no ar (padrão `http://localhost:3000`; outro endereço com `BASE_URL`):
o teste `@smoke` faz, **só com o teclado**, login como `operador` / `Operador@123` → conta `CC-0001` → registra um
crédito de valor único → vê a confirmação e o lançamento no extrato → abre o consolidado do período (inclui hoje) e
vê "Dados atualizados até", verificando com `@axe-core/playwright` que login, conta e consolidado não têm violações
WCAG 2.1 AA (`wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`). Outro teste entra como `consulta` / `Consulta@123` e
confere que os botões de registrar e estornar não aparecem.

```bash
docker compose up --build -d                 # todos os serviços healthy (inclui o rebuild do web)
npm --prefix web ci
npx --prefix web playwright install chromium # uma vez por máquina
npm --prefix web run test:e2e                # só o smoke: npm --prefix web run test:e2e -- --grep @smoke
```

Cada execução cria um lançamento de crédito novo em `CC-0001` (dados de demonstração). O relatório HTML fica em
`web/playwright-report/`.

### Teste de carga do consolidado (k6)

`tests/carga/consolidado.js` aplica 50 req/s por 2 min (`constant-arrival-rate`) às consultas de consolidado diário
(últimos 30/90/365 dias e meses fechados do último ano) e de saldo (atual e em instantes passados) nas contas
CC-0001..CC-0005, com o token do usuário `consulta` obtido no login. Thresholds: `http_req_failed < 5%`,
`p(95) < 300 ms` e `dropped_iterations` ≤ 5% das iterações esperadas. O serviço `k6` (`grafana/k6:1.3.0`, profile
`carga`) usa as URLs internas do compose e grava os sumários em `docs/teste-carga/<cenário>.json` e `.txt`.
Resultados medidos e análise: [`docs/nfr.md`](docs/nfr.md).

```bash
docker compose up --build -d                     # aguarde todos healthy e o worker projetar o seed (1–2 min)
docker compose --profile carga run --rm k6       # cenário normal; sai com código 99 se algum threshold falhar

# Com lançamentos registrados em paralelo (10 req/s como operador; o worker projeta e invalida o cache):
K6_LANCAMENTOS_PARALELOS=1 K6_NOME_CENARIO=consolidado-com-lancamentos docker compose --profile carga run --rm k6

# Degradado, com o Redis parado (--no-deps impede o compose de religar o Redis, dependência da API):
docker compose stop redis
K6_NOME_CENARIO=consolidado-redis-parado docker compose --profile carga run --rm --no-deps k6
docker compose start redis
```

No PowerShell, defina as variáveis antes (`$env:K6_DURACAO = "30s"`) e remova depois (`Remove-Item Env:K6_DURACAO`).

| Variável | Padrão | Uso |
|---|---|---|
| `K6_DURACAO` | `2m` | Duração da carga (ex.: `30s` para uma rodada rápida) |
| `K6_TAXA` | `50` | Consultas por segundo |
| `K6_NOME_CENARIO` | `consolidado` | Nome dos arquivos de sumário em `docs/teste-carga/` |
| `K6_LANCAMENTOS_PARALELOS` | `0` | `1` habilita o cenário de lançamentos em paralelo |
| `K6_TAXA_LANCAMENTOS` | `10` | Lançamentos por segundo no cenário paralelo |
| `K6_LIMITE_P95_MS` | `300` | Limite do p95 (só altere para um experimento documentado; a meta é 300) |
| `K6_USUARIO` / `K6_SENHA` | `consulta` / `IDENTIDADE_SENHA_CONSULTA` | Usuário das consultas |
| `K6_USUARIO_OPERADOR` / `K6_SENHA_OPERADOR` | `operador` / `IDENTIDADE_SENHA_OPERADOR` | Usuário dos lançamentos paralelos |

A primeira rodada logo após subir (ou reiniciar) a `consolidado-api` mede a partida a frio e costuma estourar o
p95 (veja `docs/nfr.md`); para comparar desempenho, rode com o ambiente já aquecido. A pasta `docs/` está no
`.gitignore` deste repositório, então os sumários gerados ficam só na máquina local.

### Teste de sistema: recuperação do consolidado

Para o `consolidado-worker` e a `consolidado-api` (via `docker compose stop`), registra 100 lançamentos (todos
devem responder `201`), sobe os dois de novo e espera (até 3 min) o saldo do consolidado bater com o saldo do
razão + a soma líquida dos 100 lançamentos. É ignorado por padrão; habilite com `BANCO_TESTES_SISTEMA=1`:

```bash
docker compose up --build -d
BANCO_TESTES_SISTEMA=1 dotnet test tests/Banco.TestesSistema
```

```powershell
$env:BANCO_TESTES_SISTEMA = "1"; dotnet test tests/Banco.TestesSistema; Remove-Item Env:BANCO_TESTES_SISTEMA
```

Variáveis opcionais: `BANCO_URL_IDENTIDADE` (padrão `http://localhost:5000`), `BANCO_URL_LANCAMENTOS`
(`http://localhost:5001`), `BANCO_URL_CONSOLIDADO` (`http://localhost:5002`), `BANCO_COMPOSE_PROJETO` (`banco`,
o `name:` do compose), `BANCO_TESTES_SISTEMA_CONTA` (`CC-0005`) e `IDENTIDADE_SENHA_OPERADOR`.

## Solução de problemas

- **Porta ocupada** (`bind: address already in use` / `port is already allocated`): outro processo usa 3000,
  5000–5002, 5432, 5672, 6379 ou 15672. Pare-o ou troque a porta publicada no `.env` (`POSTGRES_PORT`,
  `REDIS_PORT`, `RABBITMQ_PORT`, `RABBITMQ_MANAGEMENT_PORT`, `IDENTIDADE_PORT`, `LANCAMENTOS_PORT`,
  `CONSOLIDADO_PORT`, `WEB_PORT`). No Windows, a porta 5000/5001 às vezes é reservada pelo sistema; veja com
  `netsh interface ipv4 show excludedportrange protocol=tcp`.
- **Primeira subida lenta ou serviço em `health: starting`:** é o download das imagens e o build; acompanhe com
  `docker compose logs -f <serviço>`. As APIs tentam de novo enquanto o Postgres ainda não aceita conexões.
- **Consolidado sem dados logo após a subida:** o worker ainda está processando o histórico do seed; aguarde 1–2
  minutos (o campo `atualizadoAte` avança).
- **`400` ao registrar pelo curl no Windows:** o Git Bash/PowerShell pode enviar acentos do `-d` fora de UTF-8
  e o JSON fica inválido. Evite acentos no exemplo ou envie um arquivo salvo em UTF-8 (`--data-binary @corpo.json`).
- **Zerar tudo** (bancos, filas, cache e chaves do JWT): `docker compose down -v` e suba de novo.
- **Testes de integração falham ao iniciar containers:** o Docker precisa estar em execução (Testcontainers).

## Documentação

| Documento | Conteúdo |
|---|---|
| [Arquitetura (modelo C4)](docs/arquitetura/README.md) | Índice: contexto, containers, componentes de cada serviço, fluxos de sequência (incluindo falhas) e a organização em camadas com a regra de dependência |
| [Decisões arquiteturais](docs/decisoes-arquiteturais.md) | Documento único com todas as decisões (D0–D16): contexto, alternativas, porquê, trade-offs e onde está no código; divergências entre design e implementação |
| [ADRs](docs/adr/README.md) | Registros curtos das decisões mais significativas (estilo, camadas, repository/UoW, outbox, projeção, cache, identidade, saldo, migrações) |
| [Requisitos não funcionais](docs/nfr.md) | Metas, como cada uma é verificada, resultados medidos do teste de carga e análise |
| [Resultados do teste de carga](docs/teste-carga/) | Sumários do k6 (`.json` e `.txt`) de cada cenário |
| [Evolução](docs/evolucao.md) | Próximos passos: PgBouncer, Keycloak/OIDC, cofre de segredos, migration job, OpenTelemetry, HA, snapshots, strangler fig |

