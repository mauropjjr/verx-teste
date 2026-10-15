# 0008 — Sem checagem de saldo suficiente no registro

- **Status:** Aceita (2026-10)
- **Decisão detalhada:** [D3](../decisoes-arquiteturais.md#d3-modelo-de-dados-e-dinheiro)

## Contexto

O desafio pede registrar débitos e créditos e consolidar o saldo. Verificar saldo antes de aceitar um débito exigiria
ler o saldo no momento do registro.

## Decisão

O serviço de Lançamentos aceita débitos mesmo que deixem a conta negativa. Limite, cheque especial e bloqueio por
saldo insuficiente pertencem a um domínio de **autorização transacional** futuro, fora do escopo.

## Consequências

- O registro não depende do Consolidado (que é eventual e pode estar fora) — preserva o requisito de disponibilidade.
- Não há serialização por conta no registro (sem lock de saldo), o que mantém o caminho de escrita simples.
- Contas podem ficar negativas; um futuro serviço de autorização precisaria de saldo forte (razão + reservas), não do
  consolidado.
