# 0003 — Repository + Unit of Work só no lado de escrita

- **Status:** Aceita (2026-10)
- **Decisão detalhada:** [D2.1](../decisoes-arquiteturais.md#d21-repository--unit-of-work-só-no-lado-de-escrita)

## Contexto

A Aplicacao não pode depender de EF Core, mas precisa gravar lançamento + outbox (e, na projeção, inbox + resumo)
na mesma transação. O `DbContext` já é um Unit of Work e os `DbSet` já são repositórios.

## Decisão

- **Escrita** (Identidade, Lançamentos, projeção do Consolidado): um repositório por agregado (`IRepositorioContas`,
  `IRepositorioLancamentos`, `IRepositorioUsuarios`, `IRepositorioResumosDiarios`, `IRegistroMensagensProcessadas`)
  e uma unidade de trabalho por contexto (`IUnidadeDeTrabalhoLancamentos`, `IUnidadeDeTrabalhoIdentidade`,
  `IUnidadeDeTrabalhoConsolidado`). Violação de UNIQUE volta como `ResultadoConfirmacao`, não como exceção.
- **Leitura** (extrato e consolidado): **sem repositório** — portas de consulta (`IConsultasExtrato`,
  `IConsultasConsolidado`) com SQL direto.

## Consequências

- Casos de uso testáveis sem banco e regra de dependência preservada (e testada).
- A unidade de trabalho é o ponto que garante outbox e inbox transacionais sem a Aplicacao conhecer EF.
- Mais interfaces; duas formas de acesso a dados por contexto (repositório na escrita, SQL na leitura).
- "Só `DbContext`" seria mais simples, mas acoplaria a Aplicacao ao EF Core.
