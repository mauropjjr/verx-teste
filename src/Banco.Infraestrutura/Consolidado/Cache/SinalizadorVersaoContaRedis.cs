using Banco.Aplicacao.Consolidado.Portas;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Banco.Infraestrutura.Consolidado.Cache;

/// <summary>
/// Incrementa <c>versao:{conta}</c> no Redis (design D9): invalidação do cache-aside sem <c>SCAN</c>/<c>DEL</c>.
/// Redis fora não impede a confirmação da mensagem — a projeção já está no Postgres e o cache tem TTL —, então a
/// falha só é registrada.
/// </summary>
public sealed partial class SinalizadorVersaoContaRedis(
    IConnectionMultiplexer redis,
    ILogger<SinalizadorVersaoContaRedis> logger) : ISinalizadorVersaoConta
{
    public async Task IncrementarAsync(string contaId, CancellationToken cancelamento)
    {
        try
        {
            await redis.GetDatabase().StringIncrementAsync(ChavesCacheConsolidado.Versao(contaId));
        }
        catch (Exception erro) when (erro is RedisException or TimeoutException)
        {
            LogFalha(logger, erro, contaId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível incrementar a versão da conta {ContaId} no Redis; o cache expira pelo TTL")]
    private static partial void LogFalha(ILogger logger, Exception erro, string contaId);
}
