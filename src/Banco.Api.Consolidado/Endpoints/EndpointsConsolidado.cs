using Banco.Aplicacao.Consolidado;
using Banco.Infraestrutura.Compartilhado.Autenticacao;
using FluentValidation;

namespace Banco.Api.Consolidado.Endpoints;

/// <summary>
/// Consultas do consolidado (spec balance-consolidation), papel de consulta: consolidado diário por período
/// (<c>GET .../consolidado-diario?de&amp;ate</c>) e saldo em um instante (<c>GET .../saldo?em</c>). Toda resposta
/// expõe <c>atualizadoAte</c> (consistência eventual explícita).
/// </summary>
internal static class EndpointsConsolidado
{
    public static IEndpointRouteBuilder MapearEndpointsConsolidado(this IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup("/api/v1/contas/{contaId}").RequireAuthorization(PoliticasAutorizacao.PodeConsultar);
        grupo.MapGet("consolidado-diario", ConsultarPeriodoAsync).WithName("ConsultarConsolidadoDiario");
        grupo.MapGet("saldo", ConsultarSaldoAsync).WithName("ConsultarSaldo");
        return endpoints;
    }

    private static async Task<IResult> ConsultarPeriodoAsync(
        string contaId,
        string? de,
        string? ate,
        IValidator<EntradaPeriodo> validador,
        ConsultarConsolidadoDiario consultar,
        CancellationToken cancelamento)
    {
        var validacao = await validador.ValidateAsync(new EntradaPeriodo(contaId, de, ate), cancelamento);
        if (!validacao.IsValid)
        {
            return validacao.ProblemaValidacao();
        }

        ValidacaoEntrada.TentarLerData(de, out var inicio);
        ValidacaoEntrada.TentarLerData(ate, out var fim);
        var periodo = await consultar.ExecutarAsync(contaId, inicio, fim, cancelamento);
        return TypedResults.Ok(new RespostaConsolidadoDiario(
            contaId,
            [.. periodo.Dias.Select(d => new RespostaDia(d.Dia, d.Creditos, d.Debitos, d.SaldoDoDia, d.Fechamento))],
            periodo.AtualizadoAte));
    }

    private static async Task<IResult> ConsultarSaldoAsync(
        string contaId,
        string? em,
        IValidator<EntradaSaldo> validador,
        ConsultarSaldo consultar,
        CancellationToken cancelamento)
    {
        var validacao = await validador.ValidateAsync(new EntradaSaldo(contaId, em), cancelamento);
        if (!validacao.IsValid)
        {
            return validacao.ProblemaValidacao();
        }

        DateTimeOffset? instante = ValidacaoEntrada.TentarLerInstante(em, out var lido) ? lido : null;
        var saldo = await consultar.ExecutarAsync(contaId, instante, cancelamento);
        return TypedResults.Ok(new RespostaSaldo(contaId, saldo.Saldo, saldo.Em, saldo.AtualizadoAte));
    }
}

/// <summary>Consolidado por período: <c>dias</c> em ordem e <c>atualizadoAte</c> (último lançamento processado da conta).</summary>
internal sealed record RespostaConsolidadoDiario(string ContaId, IReadOnlyList<RespostaDia> Dias, DateTimeOffset? AtualizadoAte);

/// <summary>Um dia: <c>dia</c>, <c>creditos</c>, <c>debitos</c>, <c>saldoDia</c> e <c>fechamento</c> acumulado.</summary>
internal sealed record RespostaDia(DateOnly Dia, decimal Creditos, decimal Debitos, decimal SaldoDia, decimal Fechamento);

/// <summary>Saldo em um instante: <c>saldo</c>, <c>em</c> e <c>atualizadoAte</c>.</summary>
internal sealed record RespostaSaldo(string ContaId, decimal Saldo, DateTimeOffset Em, DateTimeOffset? AtualizadoAte);
