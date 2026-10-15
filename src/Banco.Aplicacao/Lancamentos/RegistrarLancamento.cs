using Banco.Aplicacao.Lancamentos.Portas;
using Banco.Dominio.Lancamentos;

namespace Banco.Aplicacao.Lancamentos;

/// <summary>
/// Caso de uso "Registrar lançamento" (spec ledger-entries): crédito ou débito em conta existente e ativa, com
/// instante atribuído pelo servidor e idempotência por <c>Idempotency-Key</c> (design D4). A corrida entre
/// requisições simultâneas com a mesma chave é decidida pela UNIQUE do banco: quem perde responde com o
/// lançamento de quem ganhou.
/// </summary>
public sealed class RegistrarLancamento(
    IRepositorioContas contas,
    IRepositorioLancamentos lancamentos,
    ICaixaSaidaEventos caixaSaida,
    IUnidadeDeTrabalhoLancamentos unidadeDeTrabalho,
    TimeProvider relogio)
{
    public async Task<ResultadoLancamento> ExecutarAsync(ComandoRegistrarLancamento comando, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(comando);
        var hash = HashRequisicao.DoRegistro(comando.Tipo, comando.Valor, comando.Descricao);

        var conta = await contas.ObterAsync(comando.ContaId, cancelamento);
        if (conta is null)
        {
            return ResultadoLancamento.Recusado(SituacaoResultadoLancamento.ContaNaoEncontrada);
        }

        if (await Idempotencia.ResultadoAnteriorAsync(lancamentos, conta.Id, comando.ChaveIdempotencia, hash, cancelamento) is { } anterior)
        {
            return anterior;
        }

        if (!conta.EstaAtiva)
        {
            return ResultadoLancamento.Recusado(SituacaoResultadoLancamento.ContaInativa);
        }

        var lancamento = Lancamento.Registrar(
            conta.Id, comando.Tipo, comando.Valor, comando.Descricao, relogio.GetUtcNow(), comando.ChaveIdempotencia, hash);
        lancamentos.Adicionar(lancamento);
        // Outbox (D5): o evento é confirmado na mesma transação do lançamento — os dois entram ou nenhum entra.
        caixaSaida.Adicionar(EventosLancamento.Registrado(lancamento));

        return await unidadeDeTrabalho.ConfirmarAsync(cancelamento) switch
        {
            ResultadoConfirmacao.Confirmado => ResultadoLancamento.Criado(lancamento),
            ResultadoConfirmacao.ChaveIdempotenciaDuplicada =>
                await Idempotencia.ResultadoDoVencedorAsync(lancamentos, conta.Id, comando.ChaveIdempotencia, hash, cancelamento),
            var outro => throw new InvalidOperationException($"Confirmação inesperada no registro: {outro}"),
        };
    }
}

/// <summary>Dados do registro, já validados quanto a formato pelo host.</summary>
public sealed record ComandoRegistrarLancamento(
    string ContaId,
    TipoLancamento Tipo,
    decimal Valor,
    string Descricao,
    string ChaveIdempotencia);
