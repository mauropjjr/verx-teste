using System.Text.Json;
using Banco.Aplicacao.Compartilhado.Contratos;
using Banco.Aplicacao.Consolidado;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Banco.Infraestrutura.Consolidado.Mensageria;

public enum ResultadoProcessamento
{
    /// <summary>Projetada (ou duplicata já projetada): confirmar (ack).</summary>
    Confirmar,

    /// <summary>Malformada, inválida ou tentativas esgotadas: rejeitar sem recolocar na fila (vai para a DLQ).</summary>
    Rejeitar,
}

/// <summary>
/// Processa o corpo de uma mensagem <c>lancamento.registrado.v1</c> (design D7/D8): desserializa o
/// <see cref="LancamentoRegistradoV1"/> e executa <see cref="ProjetarLancamento"/> num escopo novo por tentativa,
/// com retry Polly (backoff exponencial + jitter) só para falhas transitórias (<see cref="ClassificadorFalhas"/>).
/// Não conhece o RabbitMQ: devolve se a mensagem deve ser confirmada ou rejeitada.
/// </summary>
public sealed partial class ProcessadorMensagemLancamento
{
    private readonly IServiceScopeFactory _fabricaEscopos;
    private readonly ILogger<ProcessadorMensagemLancamento> _logger;
    private readonly ResiliencePipeline _retentativas;

    public ProcessadorMensagemLancamento(
        IServiceScopeFactory fabricaEscopos,
        OpcoesConsumidorConsolidado opcoes,
        ILogger<ProcessadorMensagemLancamento> logger)
    {
        ArgumentNullException.ThrowIfNull(opcoes);
        _fabricaEscopos = fabricaEscopos;
        _logger = logger;
        _retentativas = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ClassificadorFalhas.EhTransitoria),
                MaxRetryAttempts = opcoes.NovasTentativas,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = opcoes.EsperaBaseRetentativa,
                OnRetry = argumentos =>
                {
                    LogRetentativa(_logger, argumentos.Outcome.Exception, argumentos.AttemptNumber + 1, argumentos.RetryDelay);
                    return default;
                },
            })
            .Build();
    }

    public async Task<ResultadoProcessamento> ProcessarAsync(ReadOnlyMemory<byte> corpo, CancellationToken cancelamento)
    {
        LancamentoRegistradoV1 evento;
        try
        {
            evento = JsonSerializer.Deserialize<LancamentoRegistradoV1>(corpo.Span, LancamentoRegistradoV1.OpcoesJson)
                ?? throw new JsonException("Corpo nulo.");
        }
        catch (JsonException erro)
        {
            LogMalformada(_logger, erro);
            return ResultadoProcessamento.Rejeitar;
        }

        try
        {
            var resultado = await _retentativas.ExecuteAsync(
                async (estado, token) =>
                {
                    await using var escopo = estado.CreateAsyncScope();
                    return await escopo.ServiceProvider.GetRequiredService<ProjetarLancamento>().ExecutarAsync(evento, token);
                },
                _fabricaEscopos,
                cancelamento);
            LogProcessada(_logger, evento.IdLancamento, evento.ContaId, resultado);
            return ResultadoProcessamento.Confirmar;
        }
        catch (EventoInvalidoException erro)
        {
            LogInvalida(_logger, erro, evento.IdLancamento);
            return ResultadoProcessamento.Rejeitar;
        }
        catch (Exception erro) when (erro is not OperationCanceledException || !cancelamento.IsCancellationRequested)
        {
            LogFalhou(_logger, erro, evento.IdLancamento, ClassificadorFalhas.EhTransitoria(erro));
            return ResultadoProcessamento.Rejeitar;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Lançamento {IdLancamento} da conta {ContaId}: {Resultado}")]
    private static partial void LogProcessada(ILogger logger, Guid idLancamento, string contaId, ResultadoProjecao resultado);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha transitória na projeção; nova tentativa {Tentativa} em {Espera}")]
    private static partial void LogRetentativa(ILogger logger, Exception? erro, int tentativa, TimeSpan espera);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mensagem malformada (não é um LancamentoRegistradoV1); enviada para a DLQ")]
    private static partial void LogMalformada(ILogger logger, Exception erro);

    [LoggerMessage(Level = LogLevel.Error, Message = "Evento {IdLancamento} inválido; enviado para a DLQ")]
    private static partial void LogInvalida(ILogger logger, Exception erro, Guid idLancamento);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao projetar o lançamento {IdLancamento} (transitória: {Transitoria}); enviado para a DLQ")]
    private static partial void LogFalhou(ILogger logger, Exception erro, Guid idLancamento, bool transitoria);
}
