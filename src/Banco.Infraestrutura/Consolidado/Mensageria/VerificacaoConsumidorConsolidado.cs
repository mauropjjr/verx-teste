using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Banco.Infraestrutura.Consolidado.Mensageria;

/// <summary>Prontidão do Worker: o consumidor está conectado ao RabbitMQ com o canal aberto.</summary>
internal sealed class VerificacaoConsumidorConsolidado(ConsumidorLancamentosRegistrados consumidor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(consumidor.EstaConsumindo ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());
}
