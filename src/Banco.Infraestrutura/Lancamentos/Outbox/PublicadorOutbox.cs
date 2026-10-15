using System.Runtime.ExceptionServices;
using System.Text;
using Banco.Infraestrutura.Compartilhado.Mensageria;
using Banco.Infraestrutura.Lancamentos.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Serilog.Context;

namespace Banco.Infraestrutura.Lancamentos.Outbox;

/// <summary>
/// Publica as <see cref="MensagemOutbox"/> pendentes no RabbitMQ (design D5/D6), em segundo plano e desacoplado da
/// requisição: o registro do lançamento nunca espera o broker.
/// <list type="bullet">
/// <item>Lotes travados com <c>FOR UPDATE SKIP LOCKED</c>: várias réplicas dividem o trabalho sem publicar a mesma
/// mensagem em paralelo.</item>
/// <item>Canal com <i>publisher confirms</i> e <c>mandatory</c>: só marca <c>publicado_em</c> depois que o broker
/// confirmou e roteou para uma fila; nack, retorno ou queda de conexão incrementa <c>tentativas</c> e aplica
/// backoff exponencial.</item>
/// <item>Mensagem persistente, <c>message_id</c> = id da mensagem = id do lançamento (pelo qual o consumidor
/// deduplica), <c>content-type</c> JSON e cabeçalho <c>x-correlation-id</c>.</item>
/// <item>Broker fora (inclusive na subida): espera e tenta reconectar; os pendentes ficam no banco.</item>
/// </list>
/// <i>At-least-once</i>: uma queda entre a confirmação e o COMMIT republica a mensagem — o inbox do Consolidado
/// neutraliza a duplicata (D7).
/// </summary>
public sealed partial class PublicadorOutbox(
    IServiceScopeFactory fabricaEscopos,
    ConexaoRabbitMq conexaoRabbitMq,
    OpcoesPublicadorOutbox opcoes,
    TimeProvider relogio,
    ILogger<PublicadorOutbox> logger) : BackgroundService
{
    public const string CabecalhoCorrelacao = TopologiaMensageria.CabecalhoCorrelacao;
    private IChannel? _canal;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var falhasSeguidas = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var canal = await ObterCanalAsync(stoppingToken);
                var publicadas = await PublicarLoteAsync(canal, stoppingToken);
                falhasSeguidas = 0;
                if (publicadas < opcoes.TamanhoLote)
                {
                    await Task.Delay(opcoes.IntervaloSondagem, relogio, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception erro)
            {
                var espera = Backoff(++falhasSeguidas);
                LogFalha(logger, erro, falhasSeguidas, espera);
                await DescartarCanalAsync();
                try
                {
                    await Task.Delay(espera, relogio, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await DescartarCanalAsync();
    }

    private async Task<IChannel> ObterCanalAsync(CancellationToken cancelamento)
    {
        if (_canal is { IsOpen: true })
        {
            return _canal;
        }

        await DescartarCanalAsync();
        var conexao = await conexaoRabbitMq.ObterAsync(cancelamento);
        // Tracking: BasicPublishAsync só retorna depois do ack do broker e lança PublishException em nack/retorno.
        var canal = await conexao.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancelamento);
        await TopologiaMensageria.DeclararAsync(canal, cancelamento);
        _canal = canal;
        return canal;
    }

    private async Task<int> PublicarLoteAsync(IChannel canal, CancellationToken cancelamento)
    {
        await using var escopo = fabricaEscopos.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<LancamentosDbContext>();
        await using var transacao = await contexto.Database.BeginTransactionAsync(cancelamento);

        var lote = await contexto.MensagensOutbox
            .FromSql($"""
                SELECT * FROM mensagens_outbox
                WHERE publicado_em IS NULL
                ORDER BY ocorrido_em
                LIMIT {opcoes.TamanhoLote}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancelamento);

        ExceptionDispatchInfo? falha = null;
        foreach (var mensagem in lote)
        {
            try
            {
                await PublicarAsync(canal, mensagem, cancelamento);
                mensagem.PublicadoEm = relogio.GetUtcNow();
            }
            catch (Exception erro) when (erro is not OperationCanceledException)
            {
                // Quase sempre é o canal/broker: para o lote, grava o que já foi confirmado e entra em backoff.
                mensagem.Tentativas++;
                falha = ExceptionDispatchInfo.Capture(erro);
                break;
            }
        }

        await contexto.SaveChangesAsync(cancelamento);
        await transacao.CommitAsync(cancelamento);
        falha?.Throw();
        return lote.Count;
    }

    private async Task PublicarAsync(IChannel canal, MensagemOutbox mensagem, CancellationToken cancelamento)
    {
        var propriedades = new BasicProperties
        {
            MessageId = mensagem.Id.ToString(),
            Type = mensagem.Tipo,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(mensagem.OcorridoEm.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>(),
        };
        if (mensagem.IdCorrelacao is not null)
        {
            propriedades.Headers[CabecalhoCorrelacao] = mensagem.IdCorrelacao;
        }

        // O tipo do evento é a routing key (contrato LancamentoRegistradoV1.NomeEvento).
        await canal.BasicPublishAsync(
            TopologiaMensageria.ExchangeLancamentos,
            mensagem.Tipo,
            mandatory: true,
            propriedades,
            Encoding.UTF8.GetBytes(mensagem.Conteudo),
            cancelamento);

        // Mesmo id de correlação da requisição nos logs da publicação (spec platform-operations).
        using (LogContext.PushProperty("CorrelationId", mensagem.IdCorrelacao))
        {
            LogPublicada(logger, mensagem.Id, mensagem.Tipo);
        }
    }

    private TimeSpan Backoff(int falhasSeguidas)
    {
        var espera = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(falhasSeguidas - 1, 10)));
        return espera > opcoes.EsperaMaximaFalha ? opcoes.EsperaMaximaFalha : espera;
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Mensagem {IdMensagem} ({Tipo}) publicada no RabbitMQ")]
    private static partial void LogPublicada(ILogger logger, Guid idMensagem, string tipo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao publicar o outbox ({FalhasSeguidas} seguidas); nova tentativa em {Espera}")]
    private static partial void LogFalha(ILogger logger, Exception erro, int falhasSeguidas, TimeSpan espera);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Falha ao descartar o canal do RabbitMQ")]
    private static partial void LogFalhaDescarte(ILogger logger, Exception erro);
}
