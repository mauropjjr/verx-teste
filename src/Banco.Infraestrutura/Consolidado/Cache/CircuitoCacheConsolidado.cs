using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;

namespace Banco.Infraestrutura.Consolidado.Cache;

/// <summary>
/// Circuit breaker (Polly) das chamadas ao Redis do cache do consolidado (design D9). Singleton: o estado do circuito
/// é do processo. Com metade das chamadas falhando (mínimo de 2 numa janela de 10 s), abre por 15 s e as consultas
/// vão direto ao Postgres sem nem tentar o Redis; depois, uma chamada de teste decide se fecha de novo. Somado aos
/// timeouts curtos e ao <c>BacklogPolicy.FailFast</c> do multiplexer, o Redis fora nunca segura uma requisição.
/// </summary>
public sealed partial class CircuitoCacheConsolidado
{
    private readonly ResiliencePipeline _pipeline;

    public CircuitoCacheConsolidado(ILogger<CircuitoCacheConsolidado> logger)
    {
        _pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<RedisException>().Handle<TimeoutException>(),
                FailureRatio = 0.5,
                MinimumThroughput = 2,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(15),
                OnOpened = argumentos =>
                {
                    LogAberto(logger, argumentos.Outcome.Exception, argumentos.BreakDuration);
                    return default;
                },
                OnClosed = _ =>
                {
                    LogFechado(logger);
                    return default;
                },
            })
            .Build();
    }

    /// <summary>Executa a chamada ao Redis; com o Redis fora (ou o circuito aberto) devolve <c>null</c>.</summary>
    public async Task<T?> TentarAsync<T>(Func<CancellationToken, ValueTask<T>> chamada, CancellationToken cancelamento)
        where T : class
    {
        try
        {
            return await _pipeline.ExecuteAsync(chamada, cancelamento);
        }
        catch (Exception erro) when (erro is BrokenCircuitException or RedisException or TimeoutException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis do cache indisponível: circuito aberto por {Duracao}; consultas servidas pelo Postgres")]
    private static partial void LogAberto(ILogger logger, Exception? erro, TimeSpan duracao);

    [LoggerMessage(Level = LogLevel.Information, Message = "Redis do cache de volta: circuito fechado")]
    private static partial void LogFechado(ILogger logger);
}
