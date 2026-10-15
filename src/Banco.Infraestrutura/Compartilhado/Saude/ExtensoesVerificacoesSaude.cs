using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Banco.Infraestrutura.Compartilhado.Saude;

/// <summary>
/// Endpoints de saúde (spec platform-operations): <c>/health/live</c> indica só que o processo está ativo;
/// <c>/health/ready</c> executa todas as verificações registradas (dependências) e responde 503 se alguma
/// falhar. Sem autenticação e sem detalhes internos (só nome e estado de cada componente).
/// </summary>
public static class ExtensoesVerificacoesSaude
{
    public const string RotaVivo = "/health/live";
    public const string RotaPronto = "/health/ready";

    /// <summary>Registra o serviço de health checks; cada Api adiciona suas dependências ao builder retornado.</summary>
    public static IHealthChecksBuilder AdicionarVerificacoesSaude(this IServiceCollection servicos) =>
        servicos.AddHealthChecks();

    public static IEndpointRouteBuilder MapearVerificacoesSaude(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(RotaVivo, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = EscreverRespostaAsync,
        }).AllowAnonymous().DisableRateLimiting();

        endpoints.MapHealthChecks(RotaPronto, new HealthCheckOptions
        {
            ResponseWriter = EscreverRespostaAsync,
        }).AllowAnonymous().DisableRateLimiting();

        return endpoints;
    }

    private static Task EscreverRespostaAsync(HttpContext contexto, HealthReport relatorio)
    {
        var corpo = new
        {
            status = relatorio.Status.ToString(),
            componentes = relatorio.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        };
        contexto.Response.ContentType = "application/json";
        return contexto.Response.WriteAsync(JsonSerializer.Serialize(corpo));
    }
}
