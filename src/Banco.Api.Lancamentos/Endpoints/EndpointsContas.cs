using Banco.Aplicacao.Lancamentos;
using Banco.Infraestrutura.Compartilhado.Autenticacao;
using Banco.Infraestrutura.Lancamentos.Persistencia;

namespace Banco.Api.Lancamentos.Endpoints;

/// <summary><c>GET /api/v1/contas</c> (spec ledger-entries "Listar contas"): titular sempre mascarado.</summary>
internal static class EndpointsContas
{
    public static IEndpointRouteBuilder MapearEndpointsContas(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/contas", ListarAsync)
            .RequireAuthorization(PoliticasAutorizacao.PodeConsultar)
            .WithName("ListarContas");
        return endpoints;
    }

    private static async Task<IResult> ListarAsync(ListarContas listarContas, CancellationToken cancelamento)
    {
        var contas = await listarContas.ExecutarAsync(cancelamento);
        return TypedResults.Ok(contas.Select(c =>
            new RespostaConta(c.Id, c.TitularMascarado, ConversoesLancamentos.ParaTexto(c.Situacao))));
    }
}

/// <summary>Conta na listagem: <c>id</c>, <c>titular</c> (mascarado) e <c>situacao</c>.</summary>
internal sealed record RespostaConta(string Id, string Titular, string Situacao);
