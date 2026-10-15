using Banco.Aplicacao.Consolidado.Portas;
using Banco.Infraestrutura.Compartilhado.Mensageria;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Banco.Infraestrutura.Consolidado.Mensageria;

/// <summary>
/// Reprocessamento da DLQ <see cref="TopologiaMensageria.FilaDlqLancamentoRegistrado"/> (design D8): uma a uma,
/// <c>basic.get</c> sem auto-ack → publica na exchange <see cref="TopologiaMensageria.ExchangeLancamentos"/> com a
/// routing key original, as mesmas propriedades (<c>message_id</c>, cabeçalhos com <c>x-correlation-id</c>) e entrega
/// persistente → espera o <i>publisher confirm</i> → só então confirma (ack) a retirada da DLQ. Se a publicação falhar,
/// a mensagem volta para a DLQ (nack com requeue) — nada se perde, no pior caso ela é reenviada duas vezes, o que o
/// inbox absorve.
/// </summary>
public sealed partial class FilaMensagensMortasRabbitMq(
    ConexaoRabbitMq conexaoRabbitMq,
    ILogger<FilaMensagensMortasRabbitMq> logger) : IFilaMensagensMortas
{
    public async Task<int> ReenviarAsync(int maximo, CancellationToken cancelamento)
    {
        var conexao = await conexaoRabbitMq.ObterAsync(cancelamento);
        await using var canal = await conexao.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancelamento);
        await TopologiaMensageria.DeclararAsync(canal, cancelamento);

        var reenviadas = 0;
        while (reenviadas < maximo
            && await canal.BasicGetAsync(TopologiaMensageria.FilaDlqLancamentoRegistrado, autoAck: false, cancelamento) is { } mensagem)
        {
            var propriedades = new BasicProperties(mensagem.BasicProperties) { DeliveryMode = DeliveryModes.Persistent };
            var routingKey = string.IsNullOrEmpty(mensagem.RoutingKey)
                ? TopologiaMensageria.RoutingKeyLancamentoRegistrado
                : mensagem.RoutingKey;
            try
            {
                // Com confirmações rastreadas, retorna só depois do ack do broker (nack/return viram exceção).
                await canal.BasicPublishAsync(
                    TopologiaMensageria.ExchangeLancamentos, routingKey, mandatory: true, propriedades, mensagem.Body, cancelamento);
            }
            catch
            {
                await canal.BasicNackAsync(mensagem.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
                throw;
            }

            await canal.BasicAckAsync(mensagem.DeliveryTag, multiple: false, cancelamento);
            reenviadas++;
        }

        LogReenviadas(logger, reenviadas, maximo);
        return reenviadas;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "DLQ reprocessada: {Reenviadas} mensagens reenviadas (máximo {Maximo})")]
    private static partial void LogReenviadas(ILogger logger, int reenviadas, int maximo);
}
