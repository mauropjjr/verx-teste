using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Banco.Infraestrutura.Consolidado.Persistencia;

/// <summary>Prontidão: o banco do consolidado aceita conexões.</summary>
internal sealed class VerificacaoBancoConsolidado(ConsolidadoDbContext contexto) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await contexto.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy();
}
