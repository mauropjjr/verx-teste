# C4 — Nível 3: Componentes da Identidade

Dentro do container `identidade-api`. O host `Banco.Api.Identidade` só compõe o contexto Identidade
(`AdicionarPadroesBanco` + `AdicionarInfraestruturaIdentidade`); as classes vêm das pastas `Identidade/` de cada
camada (`Banco.Dominio`, `Banco.Aplicacao`, `Banco.Infraestrutura`) e de `Compartilhado/` da Infraestrutura.

```mermaid
C4Component
  title Componentes - identidade-api

  Container(web, "Portal web", "Next.js", "Server Action entrar")
  Container(apis, "Api.Lancamentos / Api.Consolidado", "ASP.NET Core", "Validam o JWT")
  ContainerDb(pgid, "Database identidade", "PostgreSQL 17", "Tabela usuarios")

  Container_Boundary(host, "identidade-api (Banco.Api.Identidade)") {
    Component(padroes, "Padrões de host", "Infraestrutura/Compartilhado", "Correlação, ProblemDetails, Serilog com FiltroDadosSensiveis, rate limit, health")
    Component(epAuth, "EndpointsAutenticacao", "Minimal API", "POST /api/v1/autenticacao/login, política login-por-ip")
    Component(epChave, "EndpointsChavePublica", "Minimal API", "GET /.well-known/openid-configuration e /.well-known/jwks.json")
    Component(caso, "AutenticarUsuario", "Aplicacao, caso de uso", "Confere senha, aplica bloqueio, persiste tentativas, emite token")
    Component(usuario, "Usuario", "Dominio, agregado", "RegistrarTentativaLogin: 5 falhas bloqueiam por 15 min")
    Component(repo, "RepositorioUsuarios + UnidadeDeTrabalhoIdentidade", "Infraestrutura, EF Core", "Portas IRepositorioUsuarios e IUnidadeDeTrabalhoIdentidade")
    Component(senha, "VerificadorSenha", "Infraestrutura", "Porta IVerificadorSenha; PasswordHasher PBKDF2-HMAC-SHA512")
    Component(emissor, "EmissorTokenJwt", "Infraestrutura", "Porta IEmissorToken; JWT RS256 de 30 min com kid")
    Component(chaves, "ProvedorChavesRsa", "Infraestrutura", "Par RSA 2048 do volume ou de variável de ambiente")
    Component(init, "InicializadorBancoIdentidade + SemeadorIdentidade", "Infraestrutura", "Migração e 3 usuários demo na subida")
  }

  Rel(web, epAuth, "Login", "HTTP/JSON")
  Rel(apis, epChave, "Discovery + JWKS", "HTTP")
  Rel(epAuth, caso, "ExecutarAsync")
  Rel(caso, repo, "Obtém e confirma")
  Rel(caso, usuario, "Regra de bloqueio")
  Rel(caso, senha, "Verificar")
  Rel(caso, emissor, "Emitir")
  Rel(emissor, chaves, "Assina com")
  Rel(epChave, chaves, "Chave pública")
  Rel(repo, pgid, "SQL", "Npgsql")
  Rel(init, pgid, "Migra e semeia", "Npgsql")

  UpdateLayoutConfig($c4ShapeInRow="3", $c4BoundaryInRow="1")
```

**Legenda:** caixa azul-clara dentro da fronteira = componente (classe ou grupo de classes) do container; caixa fora
da fronteira = outro container que o usa; cilindro = database. A camada de cada componente está na tecnologia
(`Dominio`, `Aplicacao`, `Infraestrutura`).

## Mesma visão em flowchart

O layout automático do `C4Component` do Mermaid sobrepõe rótulos; esta visão tem os mesmos componentes, agrupados
por camada (da esquerda para a direita: host → Aplicacao → Dominio/Infraestrutura).

```mermaid
flowchart LR
  web["Portal web"]:::ext
  apis["Api.Lancamentos /<br/>Api.Consolidado"]:::ext
  subgraph host["Banco.Api.Identidade (host)"]
    epAuth["EndpointsAutenticacao<br/><small>POST /api/v1/autenticacao/login</small>"]:::host
    epChave["EndpointsChavePublica<br/><small>/.well-known/*</small>"]:::host
  end
  subgraph app["Banco.Aplicacao/Identidade"]
    caso["AutenticarUsuario"]:::app
  end
  subgraph dom["Banco.Dominio/Identidade"]
    usuario["Usuario"]:::dom
  end
  subgraph infra["Banco.Infraestrutura/Identidade"]
    repo["RepositorioUsuarios +<br/>UnidadeDeTrabalhoIdentidade"]:::infra
    senha["VerificadorSenha"]:::infra
    emissor["EmissorTokenJwt"]:::infra
    chaves["ProvedorChavesRsa"]:::infra
    init["InicializadorBancoIdentidade +<br/>SemeadorIdentidade"]:::infra
  end
  pgid[("database identidade")]:::banco

  web --> epAuth --> caso
  apis -.-> epChave --> chaves
  caso --> usuario
  caso -- "IRepositorioUsuarios /<br/>IUnidadeDeTrabalhoIdentidade" --> repo --> pgid
  caso -- "IVerificadorSenha" --> senha
  caso -- "IEmissorToken" --> emissor --> chaves
  init --> pgid

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
| Padrões de host (`ExtensoesPadroesBanco`) | `MiddlewareCorrelacao` (`X-Correlation-Id`), exceções → Problem Details sem detalhe interno, Serilog JSON com `FiltroDadosSensiveis` (mascara `Authorization`, senha, tokens, cookies), `RateLimiter`, `/health/live` e `/health/ready` | `Banco.Infraestrutura/Compartilhado` |
| `EndpointsAutenticacao` | `POST /api/v1/autenticacao/login` anônimo, com `RequireRateLimiting(login-por-ip)` (30/min por IP); qualquer recusa responde `401` com a mesma mensagem | `Banco.Api.Identidade/Endpoints` |
| `EndpointsChavePublica` | `/.well-known/openid-configuration` (mínimo: `issuer`, `jwks_uri`) e `/.well-known/jwks.json` | `Banco.Api.Identidade/Endpoints` |
| `AutenticarUsuario` | Caso de uso do login. Devolve `null` para usuário inexistente, senha errada ou bloqueado — o chamador não distingue | `Banco.Aplicacao/Identidade` |
| `Usuario` | Agregado com `LimiteFalhasConsecutivas = 5` e `DuracaoBloqueio = 15 min` | `Banco.Dominio/Identidade` |
| `RepositorioUsuarios`, `UnidadeDeTrabalhoIdentidade` | Implementam `IRepositorioUsuarios` (busca por login normalizado) e `IUnidadeDeTrabalhoIdentidade` sobre `IdentidadeDbContext` | EF Core + Npgsql |
| `VerificadorSenha` | Implementa `IVerificadorSenha` com o `PasswordHasher<Usuario>` do ASP.NET Core Identity (formato V3: PBKDF2-HMAC-SHA512, 100 mil iterações, salt) — só o hasher, sem os stores do Identity | `Microsoft.AspNetCore.Identity` |
| `EmissorTokenJwt` | Implementa `IEmissorToken`: `iss=banco-identidade`, `aud=banco-api`, `sub`, `name`, uma claim `role` por papel, `kid` no header, validade 30 min | `Microsoft.IdentityModel.JsonWebTokens` (`JsonWebTokenHandler`) |
| `ProvedorChavesRsa` | `Identidade:Chaves:ChavePrivadaPem` (precedência) ou arquivo `/app/chaves/identidade-rsa.pem` gerado na primeira subida; `kid` = thumbprint JWK (RFC 7638) | RSA 2048 |
| `InicializadorBancoIdentidade`, `SemeadorIdentidade` | `MigrateAsync` com retry e seed idempotente de `consulta`, `operador`, `admin` (senhas de `Identidade:UsuariosDemo`) | EF Core migrations |

## Relações

| Relação | Descrição |
|---|---|
| Portal → `EndpointsAutenticacao` | A Server Action envia login e senha; recebe `tokenAcesso` e `expiraEm` e grava o cookie `sessao` com `maxAge` = expiração |
| APIs → `EndpointsChavePublica` | O `JwtBearer` das APIs lê o discovery uma vez e cacheia as chaves; um `kid` desconhecido força nova leitura |
| `AutenticarUsuario` → `RepositorioUsuarios`/UoW | Grava o novo estado de tentativas **antes** de decidir a resposta — a contagem de falhas não se perde |
| `AutenticarUsuario` → `EmissorTokenJwt` → `ProvedorChavesRsa` | Assinatura assimétrica: só a Identidade tem a chave privada |
| `InicializadorBancoIdentidade` → database | Migração na subida (D13); em produção seria um *migration job* |
