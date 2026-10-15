using RabbitMQ.Client;

namespace Banco.Infraestrutura.Compartilhado.Mensageria;

/// <summary>
/// Topologia RabbitMQ do design D6, declarada de forma idempotente por quem publica (Lançamentos) e por quem
/// consome (Consolidado): exchange topic durável <see cref="ExchangeLancamentos"/> → routing key
/// <c>lancamento.registrado.v1</c> → fila quorum <see cref="FilaLancamentoRegistrado"/>, cujas mensagens rejeitadas
/// seguem pela DLX <see cref="ExchangeDlx"/> (fanout) para <see cref="FilaDlqLancamentoRegistrado"/>.
/// Redeclarar com os mesmos argumentos não altera nada; mudar um argumento de fila existente é recusado pelo
/// broker (PRECONDITION_FAILED) — por isso os argumentos vivem só aqui.
/// </summary>
public static class TopologiaMensageria
{
    public const string ExchangeLancamentos = "lancamentos.eventos";
    public const string RoutingKeyLancamentoRegistrado = "lancamento.registrado.v1";
    public const string FilaLancamentoRegistrado = "consolidado.lancamento-registrado";
    public const string ExchangeDlx = "consolidado.dlx";
    public const string FilaDlqLancamentoRegistrado = "consolidado.lancamento-registrado.dlq";

    /// <summary>Cabeçalho AMQP com o id de correlação da requisição de origem (spec platform-operations).</summary>
    public const string CabecalhoCorrelacao = "x-correlation-id";

    public static async Task DeclararAsync(IChannel canal, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(canal);

        await canal.ExchangeDeclareAsync(ExchangeLancamentos, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancelamento);
        await canal.ExchangeDeclareAsync(ExchangeDlx, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: cancelamento);

        await canal.QueueDeclareAsync(
            FilaDlqLancamentoRegistrado,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: cancelamento);
        await canal.QueueBindAsync(FilaDlqLancamentoRegistrado, ExchangeDlx, routingKey: string.Empty, cancellationToken: cancelamento);

        await canal.QueueDeclareAsync(
            FilaLancamentoRegistrado,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = ExchangeDlx,
            },
            cancellationToken: cancelamento);
        await canal.QueueBindAsync(FilaLancamentoRegistrado, ExchangeLancamentos, RoutingKeyLancamentoRegistrado, cancellationToken: cancelamento);
    }
}
