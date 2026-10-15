using Banco.Aplicacao.Consolidado;
using Banco.Infraestrutura.Compartilhado.Autenticacao;

namespace Banco.Api.Consolidado.Endpoints;

/// <summary>
/// Administração (spec balance-consolidation "Reprocessar DLQ", access-control "Autorização por papéis"): só o papel
/// <c>admin</c>. <c>POST /api/v1/admin/dlq/reprocessar?maximo=N</c> (padrão 100, até 1000) devolve as mensagens da DLQ
/// à fila principal e informa quantas foram reenviadas.
/// </summary>
internal static class EndpointsAdministracao
{
    public static IEndpointRouteBuilder MapearEndpointsAdministracao(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/admin/dlq/reprocessar", ReprocessarAsync)
            .RequireAuthorization(PoliticasAutorizacao.PodeAdministrar)
            .WithName("ReprocessarDlq");
        return endpoints;
    }

    private static async Task<IResult> ReprocessarAsync(int? maximo, ReprocessarDlq reprocessar, CancellationToken cancelamento)
    {
        if (maximo is { } valor && !ReprocessarDlq.MaximoValido(valor))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["maximo"] = [$"O máximo deve estar entre 1 e {ReprocessarDlq.MaximoPermitido}."],
            });
        }

        return TypedResults.Ok(new RespostaReprocessamento(await reprocessar.ExecutarAsync(maximo, cancelamento)));
    }
}

/// <summary>Resultado do reprocessamento: <c>reenviadas</c>.</summary>
internal sealed record RespostaReprocessamento(int Reenviadas);
