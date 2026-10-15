using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Banco.Infraestrutura.Identidade.Persistencia;

/// <summary>Prontidão (<c>/health/ready</c>): o banco da identidade aceita conexões.</summary>
internal sealed class VerificacaoBancoIdentidade(IdentidadeDbContext contexto) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await contexto.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy();
}
