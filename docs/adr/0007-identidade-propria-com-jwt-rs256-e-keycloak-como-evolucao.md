# 0007 — Identidade própria com JWT RS256; Keycloak como evolução

- **Status:** Aceita (2026-10) — a ser substituída por um IdP OIDC (Keycloak) em produção
- **Decisão detalhada:** [D10](../decisoes-arquiteturais.md#d10-identidade-jwt-próprio-hoje-keycloak-amanhã)

## Contexto

As APIs precisam de autenticação e papéis. Keycloak no compose aumentaria tempo de subida e memória na máquina do
avaliador e traria o problema de hostname dentro × fora do Docker (emissor diferente para o navegador e para as APIs).

## Decisão

`identidade-api` própria: hash PBKDF2 (`PasswordHasher`), bloqueio após 5 falhas por 15 min, rate limit de login por
IP, JWT RS256 de 30 min com `kid`, publicado em `/.well-known/openid-configuration` + JWKS. As APIs validam pelo
discovery (`Autenticacao:EnderecoMetadados`), com emissor, audiência, assinatura e expiração; policies `PodeConsultar`,
`PodeOperar`, `PodeAdministrar`. Sem refresh token.

## Consequências

- Sem MFA, federação, refresh ou revogação — o token curto mitiga.
- Migrar para Keycloak (Authorization Code + PKCE, MFA, AD/LDAP, refresh/revogação) é troca de configuração
  (`EnderecoMetadados`, `Emissor`, `Audiencia`) e mapeamento de papéis (`realm_access.roles` → `role`), sem mudar
  regra de negócio; o portal troca a Server Action de login pelo fluxo OIDC.
- Chave RSA local; produção: cofre/HSM com rotação (o `kid` já permite várias chaves no JWKS).
