using System.Text;
using Banco.Infraestrutura.Compartilhado.Mensageria;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;

namespace Banco.Infraestrutura.Consolidado.Mensageria;

/// <summary>
/// Consome <see cref="TopologiaMensageria.FilaLancamentoRegistrado"/> (design D6/D8):
/// <list type="bullet">
/// <item><i>manual ack</i> e <c>prefetch</c> configurável; a mensagem só é confirmada depois que a projeção foi
/// gravada (e a versão da conta sinalizada) — queda antes disso a devolve à fila, e o inbox neutraliza a repetição;</item>
/// <item>malformada, inválida ou com as tentativas transitórias esgotadas ⇒ <c>nack(requeue: false)</c> ⇒ DLX ⇒ DLQ,
/// sem bloquear as seguintes;</item>
/// <item>o cabeçalho <c>x-correlation-id</c> entra no contexto de todos os logs do processamento;</item>
/// <item>broker fora na subida: tenta de novo com backoff; quedas depois disso ficam com a <i>automatic recovery</i>
/// da conexão; canal fechado com a conexão de pé (erro de canal) é reaberto.</item>
/// </list>
/// </summary>
public sealed partial class ConsumidorLancamentosRegistrados(
    ConexaoRabbitMq conexaoRabbitMq,
    ProcessadorMensagemLancamento processador,
    OpcoesConsumidorConsolidado opcoes,
    TimeProvider relogio,
    ILogger<ConsumidorLancamentosRegistrados> logger) : BackgroundService
{
    private IChannel? _canal;
    private CancellationToken _parada;

    /// <summary>Canal aberto e consumindo (base do health check do Worker).</summary>
    public bool EstaConsumindo => _canal is { IsOpen: true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _parada = stoppingToken;
        var falhasSeguidas = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var conexao = await conexaoRabbitMq.ObterAsync(stoppingToken);
                _canal = await AbrirCanalAsync(conexao, stoppingToken);
                falhasSeguidas = 0;
                LogConsumindo(logger, TopologiaMensageria.FilaLancamentoRegistrado, opcoes.Prefetch);

                // Enquanto a conexão se recupera sozinha o canal também volta; só reabre em erro de canal.
                while (_canal.IsOpen || !conexao.IsOpen)
                {
                    await Task.Delay(opcoes.IntervaloVerificacaoCanal, relogio, stoppingToken);
                }

                LogCanalFechado(logger, _canal.CloseReason?.ReplyText);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception erro)
            {
                var espera = Backoff(++falhasSeguidas);
                LogFalhaConexao(logger, erro, falhasSeguidas, espera);
                try
                {
                    await Task.Delay(espera, relogio, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                await DescartarCanalAsync();
            }
        }
    }

    private async Task<IChannel> AbrirCanalAsync(IConnection conexao, CancellationToken cancelamento)
    {
        var canal = await conexao.CreateChannelAsync(cancellationToken: cancelamento);
        try
        {
            await TopologiaMensageria.DeclararAsync(canal, cancelamento);
            await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: opcoes.Prefetch, global: false, cancelamento);
            var consumidor = new AsyncEventingBasicConsumer(canal);
            consumidor.ReceivedAsync += (_, entrega) => AoReceberAsync(canal, entrega);
            await canal.BasicConsumeAsync(TopologiaMensageria.FilaLancamentoRegistrado, autoAck: false, consumidor, cancelamento);
            return canal;
        }
        catch
        {
            await canal.DisposeAsync();
            throw;
        }
    }

    private async Task AoReceberAsync(IChannel canal, BasicDeliverEventArgs entrega)
    {
        using (LogContext.PushProperty("CorrelationId", LerCorrelacao(entrega.BasicProperties)))
        using (LogContext.PushProperty("IdMensagem", entrega.BasicProperties.MessageId))
        {
            ResultadoProcessamento resultado;
            try
            {
                resultado = await processador.ProcessarAsync(entrega.Body, _parada);
            }
            catch (OperationCanceledException) when (_parada.IsCancellationRequested)
            {
                // Parando: sem ack nem nack, a mensagem volta para a fila quando o canal fechar.
                return;
            }

            if (resultado == ResultadoProcessamento.Confirmar)
            {
                await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, CancellationToken.None);
            }
            else
            {
                await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
            }
        }
    }

    private static string? LerCorrelacao(IReadOnlyBasicProperties propriedades) =>
        propriedades.Headers?.TryGetValue(TopologiaMensageria.CabecalhoCorrelacao, out var valor) == true
            ? valor switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string texto => texto,
                _ => null,
            }
            : null;

    private TimeSpan Backoff(int falhasSeguidas)
    {
        var espera = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(falhasSeguidas - 1, 10)));
        return espera > opcoes.EsperaMaximaReconexao ? opcoes.EsperaMaximaReconexao : espera;
    }

    private async Task DescartarCanalAsync()
    {
        if (_canal is null)
        {
            return;
        }

        try
        {
            await _canal.DisposeAsync();
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            LogFalhaDescarte(logger, erro);
        }

        _canal = null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumindo {Fila} (prefetch {Prefetch})")]
    private static partial void LogConsumindo(ILogger logger, string fila, ushort prefetch);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Canal do consumidor fechado ({Motivo}); reabrindo")]
    private static partial void LogCanalFechado(ILogger logger, string? motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao conectar o consumidor ao RabbitMQ ({FalhasSeguidas} seguidas); nova tentativa em {Espera}")]
    private static partial void LogFalhaConexao(ILogger logger, Exception erro, int falhasSeguidas, TimeSpan espera);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Falha ao descartar o canal do consumidor")]
    private static partial void LogFalhaDescarte(ILogger logger, Exception erro);
}
