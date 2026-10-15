using Banco.Aplicacao.Lancamentos.Portas;

namespace Banco.Aplicacao.Lancamentos;

/// <summary>
/// Caso de uso "Estorno de lançamento" (spec ledger-entries): cria o lançamento compensatório na mesma conta.
/// Idempotente pela <c>Idempotency-Key</c> (design D4); um lançamento é estornado no máximo uma vez — a
/// verificação prévia dá a resposta comum e a UNIQUE de <c>estorno_de</c> decide a corrida.
/// </summary>
public sealed class EstornarLancamento(
    IRepositorioContas contas,
    IRepositorioLancamentos lancamentos,
    ICaixaSaidaEventos caixaSaida,
    IUnidadeDeTrabalhoLancamentos unidadeDeTrabalho,
    TimeProvider relogio)
{
    public async Task<ResultadoLancamento> ExecutarAsync(ComandoEstornarLancamento comando, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(comando);
        var hash = HashRequisicao.DoEstorno(comando.LancamentoId);

        var conta = await contas.ObterAsync(comando.ContaId, cancelamento);
        if (conta is null)
        {
            return ResultadoLancamento.Recusado(SituacaoResultadoLancamento.ContaNaoEncontrada);
        }

        if (await Idempotencia.ResultadoAnteriorAsync(lancamentos, conta.Id, comando.ChaveIdempotencia, hash, cancelamento) is { } anterior)
        {
            return anterior;
        }

        var original = await lancamentos.ObterAsync(conta.Id, comando.LancamentoId, cancelamento);
        if (original is null)
        {
            return ResultadoLancamento.Recusado(SituacaoResultadoLancamento.LancamentoNaoEncontrado);
        }

        if (!conta.EstaAtiva)
        {
            return ResultadoLancamento.Recusado(SituacaoResultadoLancamento.ContaInativa);
        }

        if (original.EhEstorno)
        {
            return ResultadoLancamento.Recusado(SituacaoResultadoLancamento.EstornoDeEstorno);
        }

        if (await lancamentos.PossuiEstornoAsync(original.Id, cancelamento))
        {
            return ResultadoLancamento.Recusado(SituacaoResultadoLancamento.JaEstornado);
        }

        var estorno = original.Estornar(relogio.GetUtcNow(), comando.ChaveIdempotencia, hash);
        lancamentos.Adicionar(estorno);
        // Outbox (D5): o evento é confirmado na mesma transação do lançamento — os dois entram ou nenhum entra.
        caixaSaida.Adicionar(EventosLancamento.Registrado(estorno));

        return await unidadeDeTrabalho.ConfirmarAsync(cancelamento) switch
        {
            ResultadoConfirmacao.Confirmado => ResultadoLancamento.Criado(estorno),
            ResultadoConfirmacao.ChaveIdempotenciaDuplicada =>
                await Idempotencia.ResultadoDoVencedorAsync(lancamentos, conta.Id, comando.ChaveIdempotencia, hash, cancelamento),
            ResultadoConfirmacao.EstornoDuplicado => ResultadoLancamento.Recusado(SituacaoResultadoLancamento.JaEstornado),
            var outro => throw new InvalidOperationException($"Confirmação inesperada no estorno: {outro}"),
        };
    }
}

/// <summary>Estorno do lançamento <see cref="LancamentoId"/> da conta <see cref="ContaId"/>.</summary>
public sealed record ComandoEstornarLancamento(string ContaId, Guid LancamentoId, string ChaveIdempotencia);
