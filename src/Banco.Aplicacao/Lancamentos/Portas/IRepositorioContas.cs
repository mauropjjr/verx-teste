using Banco.Dominio.Lancamentos;

namespace Banco.Aplicacao.Lancamentos.Portas;

/// <summary>Repositório do agregado <see cref="Conta"/> (design D2.1).</summary>
public interface IRepositorioContas
{
    /// <summary>Busca pelo identificador; <c>null</c> se não existir.</summary>
    Task<Conta?> ObterAsync(string id, CancellationToken cancelamento);

    /// <summary>Todas as contas, ordenadas pelo identificador.</summary>
    Task<IReadOnlyList<Conta>> ListarAsync(CancellationToken cancelamento);

    void Adicionar(Conta conta);
}
