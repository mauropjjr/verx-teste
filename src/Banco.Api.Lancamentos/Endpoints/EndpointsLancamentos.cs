using Banco.Aplicacao.Lancamentos;
using Banco.Aplicacao.Lancamentos.Portas;
using Banco.Dominio.Lancamentos;
using Banco.Infraestrutura.Compartilhado.Autenticacao;
using Banco.Infraestrutura.Lancamentos.Persistencia;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Banco.Api.Lancamentos.Endpoints;

/// <summary>
/// Lançamentos de uma conta (spec ledger-entries): extrato (<c>GET .../lancamentos</c>, papel de consulta),
/// registro (<c>POST .../lancamentos</c>) e estorno (<c>POST .../lancamentos/{id}/estorno</c>), estes com papel
/// operador/admin e <c>Idempotency-Key</c> obrigatório. Recusas de negócio viram Problem Details: conta ou lançamento inexistente 404, segundo estorno
/// 409, conteúdo diferente para a mesma chave / estorno de estorno / conta inativa 422.
/// </summary>
internal static class EndpointsLancamentos
{
    public static IEndpointRouteBuilder MapearEndpointsLancamentos(this IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup("/api/v1/contas/{contaId}/lancamentos");
        grupo.MapGet("", ConsultarExtratoAsync)
            .RequireAuthorization(PoliticasAutorizacao.PodeConsultar)
            .WithName("ConsultarExtrato");
        grupo.MapPost("", RegistrarAsync)
            .RequireAuthorization(PoliticasAutorizacao.PodeOperar)
            .WithName("RegistrarLancamento");
        grupo.MapPost("{id:guid}/estorno", EstornarAsync)
            .RequireAuthorization(PoliticasAutorizacao.PodeOperar)
            .WithName("EstornarLancamento");
        return endpoints;
    }

    private static async Task<IResult> ConsultarExtratoAsync(
        string contaId,
        string? de,
        string? ate,
        int? limite,
        string? cursor,
        IValidator<EntradaExtrato> validador,
        ConsultarExtrato consultar,
        CancellationToken cancelamento)
    {
        var validacao = await validador.ValidateAsync(new EntradaExtrato(contaId, de, ate, limite, cursor), cancelamento);
        if (!validacao.IsValid)
        {
            return validacao.ProblemaValidacao();
        }

        CursorExtrato.TentarDecodificar(cursor, out var posicao);
        var pagina = await consultar.ExecutarAsync(
            new ConsultaExtrato(
                contaId,
                ValidacaoEntrada.TentarLerData(de, out var dataInicial) ? dataInicial : null,
                ValidacaoEntrada.TentarLerData(ate, out var dataFinal) ? dataFinal : null,
                limite ?? ConsultarExtrato.LimitePadrao,
                posicao),
            cancelamento);

        return pagina is null
            ? Problema(StatusCodes.Status404NotFound, "Conta não encontrada", "A conta informada não existe.")
            : TypedResults.Ok(new RespostaExtrato([.. pagina.Itens.Select(RespostaLancamento.De)], pagina.ProximoCursor));
    }

    private static async Task<IResult> RegistrarAsync(
        string contaId,
        [FromHeader(Name = ValidacaoEntrada.CabecalhoIdempotencia)] string? chaveIdempotencia,
        [FromBody] RequisicaoLancamento? corpo,
        IValidator<EntradaRegistro> validador,
        RegistrarLancamento registrar,
        CancellationToken cancelamento)
    {
        var entrada = new EntradaRegistro(contaId, chaveIdempotencia, corpo);
        var validacao = await validador.ValidateAsync(entrada, cancelamento);
        if (!validacao.IsValid)
        {
            return validacao.ProblemaValidacao();
        }

        var resultado = await registrar.ExecutarAsync(
            new ComandoRegistrarLancamento(
                contaId,
                ConversoesLancamentos.ParaTipo(corpo!.Tipo!),
                corpo.Valor!.Value,
                corpo.Descricao!,
                chaveIdempotencia!),
            cancelamento);
        return ParaHttp(resultado);
    }

    private static async Task<IResult> EstornarAsync(
        string contaId,
        Guid id,
        [FromHeader(Name = ValidacaoEntrada.CabecalhoIdempotencia)] string? chaveIdempotencia,
        IValidator<EntradaEstorno> validador,
        EstornarLancamento estornar,
        CancellationToken cancelamento)
    {
        var validacao = await validador.ValidateAsync(new EntradaEstorno(contaId, chaveIdempotencia), cancelamento);
        if (!validacao.IsValid)
        {
            return validacao.ProblemaValidacao();
        }

        var resultado = await estornar.ExecutarAsync(new ComandoEstornarLancamento(contaId, id, chaveIdempotencia!), cancelamento);
        return ParaHttp(resultado);
    }

    private static IResult ParaHttp(ResultadoLancamento resultado) => resultado.Situacao switch
    {
        SituacaoResultadoLancamento.Criado or SituacaoResultadoLancamento.Repetido =>
            TypedResults.Created((string?)null, RespostaLancamento.De(resultado.Lancamento!)),
        SituacaoResultadoLancamento.ContaNaoEncontrada =>
            Problema(StatusCodes.Status404NotFound, "Conta não encontrada", "A conta informada não existe."),
        SituacaoResultadoLancamento.LancamentoNaoEncontrado =>
            Problema(StatusCodes.Status404NotFound, "Lançamento não encontrado", "O lançamento informado não existe nesta conta."),
        SituacaoResultadoLancamento.ContaInativa =>
            Problema(StatusCodes.Status422UnprocessableEntity, "Conta inativa", "A conta informada não aceita lançamentos."),
        SituacaoResultadoLancamento.ChaveReutilizadaComOutroConteudo =>
            Problema(
                StatusCodes.Status422UnprocessableEntity,
                "Idempotency-Key reutilizada",
                "Esta Idempotency-Key já foi usada nesta conta com outro conteúdo."),
        SituacaoResultadoLancamento.JaEstornado =>
            Problema(StatusCodes.Status409Conflict, "Lançamento já estornado", "Um lançamento só pode ser estornado uma vez."),
        SituacaoResultadoLancamento.EstornoDeEstorno =>
            Problema(StatusCodes.Status422UnprocessableEntity, "Estorno não estornável", "Um lançamento de estorno não pode ser estornado."),
        _ => throw new InvalidOperationException($"Situação não mapeada: {resultado.Situacao}"),
    };

    private static ProblemHttpResult Problema(int status, string titulo, string detalhe) =>
        TypedResults.Problem(statusCode: status, title: titulo, detail: detalhe);
}

/// <summary>Lançamento como sai da API (spec: <c>id</c>, <c>contaId</c>, <c>tipo</c>, <c>valor</c>, <c>descricao</c>, <c>ocorridoEm</c>).</summary>
internal sealed record RespostaLancamento(
    Guid Id,
    string ContaId,
    string Tipo,
    decimal Valor,
    string Descricao,
    DateTimeOffset OcorridoEm,
    Guid? EstornoDe)
{
    public static RespostaLancamento De(Lancamento lancamento) => new(
        lancamento.Id,
        lancamento.ContaId,
        ConversoesLancamentos.ParaTexto(lancamento.Tipo),
        lancamento.Valor,
        lancamento.Descricao,
        lancamento.OcorridoEm,
        lancamento.EstornoDe);

    public static RespostaLancamento De(ItemExtrato item) => new(
        item.Id,
        item.ContaId,
        ConversoesLancamentos.ParaTexto(item.Tipo),
        item.Valor,
        item.Descricao,
        item.OcorridoEm,
        item.EstornoDe);
}

/// <summary>Página do extrato: <c>itens</c> (mais recentes primeiro) e <c>proximoCursor</c> (<c>null</c> na última).</summary>
internal sealed record RespostaExtrato(IReadOnlyList<RespostaLancamento> Itens, string? ProximoCursor);
