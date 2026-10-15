using Banco.Aplicacao.Lancamentos.Portas;
using Banco.Dominio.Lancamentos;

namespace Banco.Aplicacao.Lancamentos;

/// <summary>
/// Regra do design D4, comum a registro e estorno: já existe lançamento com a chave na conta ⇒ mesmo hash
/// devolve o original, hash diferente é recusado.
/// </summary>
internal static class Idempotencia
{
    public static async Task<ResultadoLancamento?> ResultadoAnteriorAsync(
        IRepositorioLancamentos lancamentos,
        string contaId,
        string chaveIdempotencia,
        string hash,
        CancellationToken cancelamento)
    {
        var existente = await lancamentos.ObterPorChaveIdempotenciaAsync(contaId, chaveIdempotencia, cancelamento);
        return existente is null ? null : Repetir(existente, hash);
    }

    /// <summary>
    /// Depois de perder a corrida na UNIQUE da chave: o vencedor já está gravado, então responde como reenvio.
    /// </summary>
    public static async Task<ResultadoLancamento> ResultadoDoVencedorAsync(
        IRepositorioLancamentos lancamentos,
        string contaId,
        string chaveIdempotencia,
        string hash,
        CancellationToken cancelamento) =>
        await ResultadoAnteriorAsync(lancamentos, contaId, chaveIdempotencia, hash, cancelamento)
            ?? throw new InvalidOperationException("Violação da chave de idempotência sem lançamento correspondente.");

    private static ResultadoLancamento Repetir(Lancamento existente, string hash) =>
        existente.HashRequisicao == hash
            ? new ResultadoLancamento(SituacaoResultadoLancamento.Repetido, existente)
            : ResultadoLancamento.Recusado(SituacaoResultadoLancamento.ChaveReutilizadaComOutroConteudo);
}
