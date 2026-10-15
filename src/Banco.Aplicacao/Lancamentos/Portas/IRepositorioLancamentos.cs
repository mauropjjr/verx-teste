using Banco.Dominio.Lancamentos;

namespace Banco.Aplicacao.Lancamentos.Portas;

/// <summary>Repositório do agregado <see cref="Lancamento"/> (design D2.1). Lançamentos são imutáveis: só se adiciona.</summary>
public interface IRepositorioLancamentos
{
    /// <summary>Lançamento <paramref name="id"/> da conta <paramref name="contaId"/>; <c>null</c> se não existir nela.</summary>
    Task<Lancamento?> ObterAsync(string contaId, Guid id, CancellationToken cancelamento);

    /// <summary>Lançamento já gravado com esta <c>Idempotency-Key</c> na conta (design D4); <c>null</c> se não houver.</summary>
    Task<Lancamento?> ObterPorChaveIdempotenciaAsync(string contaId, string chaveIdempotencia, CancellationToken cancelamento);

    /// <summary>Se o lançamento <paramref name="id"/> já tem estorno.</summary>
    Task<bool> PossuiEstornoAsync(Guid id, CancellationToken cancelamento);

    void Adicionar(Lancamento lancamento);
}
