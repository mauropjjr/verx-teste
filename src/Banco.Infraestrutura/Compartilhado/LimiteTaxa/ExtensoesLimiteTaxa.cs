using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.Infraestrutura.Compartilhado.LimiteTaxa;

/// <summary>
/// Limite de taxa (design D11, spec access-control "Limite de taxa por cliente"):
/// limitador global <i>token bucket</i> particionado pelo <c>sub</c> do token (anônimo ⇒ por IP) e a
/// política nomeada <see cref="PoliticasLimiteTaxa.LoginPorIp"/>. Excedeu ⇒ <c>429</c> com <c>Retry-After</c>.
/// Configuração (seção <c>LimiteTaxa</c>): <c>RequisicoesPorSegundo</c> (padrão 100, folga sobre o pico de
/// 50 req/s do consolidado) e <c>LoginPorIpPorMinuto</c> (padrão 30).
/// </summary>
public static class ExtensoesLimiteTaxa
{
    public const string SecaoConfiguracao = "LimiteTaxa";
    public const int RequisicoesPorSegundoPadrao = 100;
    public const int LoginPorIpPorMinutoPadrao = 30;

    public static IServiceCollection AdicionarLimiteTaxaBanco(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        var secao = configuracao.GetSection(SecaoConfiguracao);
        var porSegundo = secao.GetValue("RequisicoesPorSegundo", RequisicoesPorSegundoPadrao);
        var loginPorMinuto = secao.GetValue("LoginPorIpPorMinuto", LoginPorIpPorMinutoPadrao);

        return servicos.AddRateLimiter(opcoes =>
        {
            opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opcoes.OnRejected = (contexto, _) =>
            {
                var espera = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? retryAfter
                    : TimeSpan.FromSeconds(1);
                var segundos = Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds));
                contexto.HttpContext.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };

            opcoes.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
                RateLimitPartition.GetTokenBucketLimiter(
                    ChaveCliente(contexto),
                    _ => Balde(porSegundo, TimeSpan.FromSeconds(1))));

            opcoes.AddPolicy(PoliticasLimiteTaxa.LoginPorIp, contexto =>
                RateLimitPartition.GetTokenBucketLimiter(
                    ChaveIp(contexto),
                    _ => Balde(loginPorMinuto, TimeSpan.FromMinutes(1))));
        });
    }

    private static TokenBucketRateLimiterOptions Balde(int limite, TimeSpan periodo) => new()
    {
        TokenLimit = limite,
        TokensPerPeriod = limite,
        ReplenishmentPeriod = periodo,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static string ChaveCliente(HttpContext contexto)
    {
        var sub = contexto.User.Identity?.IsAuthenticated == true ? contexto.User.FindFirst("sub")?.Value : null;
        return sub is not null ? $"sub:{sub}" : ChaveIp(contexto);
    }

    private static string ChaveIp(HttpContext contexto) =>
        $"ip:{contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido"}";
}
