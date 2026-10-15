using Banco.Dominio.Lancamentos;

namespace Banco.Aplicacao.Lancamentos;

/// <summary>
/// Desfecho de registrar/estornar: cada recusa de negócio é um valor (o host traduz para 404/409/422), sem
/// exceção para controlar fluxo. <see cref="Lancamento"/> vem preenchido em <c>Criado</c> e <c>Repetido</c>.
/// </summary>
public sealed record ResultadoLancamento(SituacaoResultadoLancamento Situacao, Lancamento? Lancamento = null)
{
    public static ResultadoLancamento Criado(Lancamento lancamento) => new(SituacaoResultadoLancamento.Criado, lancamento);

    public static ResultadoLancamento Recusado(SituacaoResultadoLancamento situacao) => new(situacao);
}

public enum SituacaoResultadoLancamento
{
    /// <summary>Lançamento novo gravado.</summary>
    Criado,

    /// <summary>Reenvio com a mesma <c>Idempotency-Key</c> e o mesmo conteúdo: devolve o original (design D4).</summary>
    Repetido,

    ContaNaoEncontrada,

    ContaInativa,

    LancamentoNaoEncontrado,

    /// <summary>Mesma <c>Idempotency-Key</c> com conteúdo diferente (design D4).</summary>
    ChaveReutilizadaComOutroConteudo,

    /// <summary>O lançamento já tem estorno.</summary>
    JaEstornado,

    /// <summary>Estornos não podem ser estornados.</summary>
    EstornoDeEstorno,
}
