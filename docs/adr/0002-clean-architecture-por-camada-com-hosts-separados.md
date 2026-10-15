# 0002 — Clean Architecture organizada por camada, com hosts separados

- **Status:** Aceita (2026-10) — decisão do autor
- **Decisão detalhada:** [D2](../decisoes-arquiteturais.md#d2-clean-architecture-organizada-por-camada-com-um-host-por-serviço)

## Contexto

Com quatro hosts .NET, é preciso escolher entre um conjunto de projetos por serviço ou projetos por camada
compartilhados. Regras de negócio devem ser testáveis sem banco, e os contextos não podem se acoplar por código.

## Decisão

Um projeto por camada — `Banco.Dominio`, `Banco.Aplicacao`, `Banco.Infraestrutura` — com pastas por contexto
(`Compartilhado`, `Identidade`, `Lancamentos`, `Consolidado`), e quatro hosts finos (`Banco.Api.Identidade`,
`Banco.Api.Lancamentos`, `Banco.Api.Consolidado`, `Banco.Worker.Consolidado`) que compõem só o seu contexto.
Regra de dependência `Hosts → Infraestrutura → Aplicacao → Dominio`.

Alternativa descartada: **projetos por serviço** (`src/Lancamentos/{Dominio,Aplicacao,...}`) — isolamento físico pelo
compilador e imagens só com o próprio código, ao custo de 13 a 16 projetos para um sistema pequeno.

## Consequências

- Menos projetos e navegação por camada simples; os processos continuam independentes.
- Cada imagem carrega código e pacotes de todos os contextos; uma mudança numa camada recompila todos os hosts.
- O isolamento entre contextos é garantido por **testes de arquitetura** (NetArchTest, `RegrasDeArquiteturaTestes`)
  e pela **composição por host**, não pela fronteira de projeto.
- Extrair um serviço para repositório próprio exige mover as pastas do contexto para novos projetos.
