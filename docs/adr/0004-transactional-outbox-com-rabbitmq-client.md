# 0004 — Transactional Outbox com RabbitMQ.Client

- **Status:** Aceita (2026-10)
- **Decisão detalhada:** [D5](../decisoes-arquiteturais.md#d5-transactional-outbox) e [D6](../decisoes-arquiteturais.md#d6-topologia-rabbitmq)

## Contexto

Gravar no Postgres e publicar no RabbitMQ não compartilham transação (*dual write*). O registro não pode depender do
broker estar no ar, e nenhum evento pode se perder.

## Decisão

O evento `LancamentoRegistradoV1` é gravado em `mensagens_outbox` na mesma transação do lançamento. O
`PublicadorOutbox` (BackgroundService da `lancamentos-api`) lê lotes de 100 com `FOR UPDATE SKIP LOCKED`, publica na
exchange `lancamentos.eventos` com publisher confirms e `mandatory`, e marca `publicado_em`; falha ⇒ backoff até
30 s. Id da mensagem = id do lançamento = `message_id`. O RabbitMQ fica **fora** do `/health/ready` da API.
`RabbitMQ.Client` direto, em vez de MassTransit (licença comercial a partir da v9), NServiceBus, CAP ou Wolverine.

## Consequências

- Broker fora não afeta o registro (`PublicacaoOutboxTestes`): os eventos ficam pendentes e saem na volta.
- Entrega *at-least-once*: queda entre o ack do broker e o COMMIT republica — neutralizado pelo inbox
  ([0005](0005-projecao-idempotente-com-inbox-e-deltas-diarios.md)).
- Ordem não garantida; latência de até ~1 s (sondagem); falta expurgo das mensagens publicadas.
