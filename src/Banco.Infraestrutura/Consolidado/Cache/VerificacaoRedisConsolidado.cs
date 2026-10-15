using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Banco.Infraestrutura.Consolidado.Cache;

/// <summary>
/// Prontidão da Api de consolidado (spec platform-operations "Dependência fora do ar"): Redis fora ⇒ componente
/// <c>redis</c> não saudável e <c>/health/ready</c> 503, enquanto as consultas seguem respondendo pelo Postgres.
/// </summary>
internal sealed class VerificacaoRedisConsolidado(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!redis.IsConnected)
        {
            return HealthCheckResult.Unhealthy("Redis desconectado; consultas servidas pelo Postgres.");
        }

        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception erro) when (erro is RedisException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Redis não respondeu; consultas servidas pelo Postgres.", erro);
        }
    }
}
