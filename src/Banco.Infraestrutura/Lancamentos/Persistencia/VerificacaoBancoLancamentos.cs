using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Banco.Infraestrutura.Lancamentos.Persistencia;

/// <summary>Prontidão (<c>/health/ready</c>): o banco de lançamentos aceita conexões.</summary>
internal sealed class VerificacaoBancoLancamentos(LancamentosDbContext contexto) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await contexto.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy();
}
