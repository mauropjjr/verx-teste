using Banco.Dominio.Identidade;

namespace Banco.Aplicacao.Identidade.Portas;

/// <summary>Repositório do agregado <see cref="Usuario"/> (design D2.1).</summary>
public interface IRepositorioUsuarios
{
    /// <summary>Busca pelo login (comparado na forma normalizada); <c>null</c> se não existir.</summary>
    Task<Usuario?> ObterPorLoginAsync(string login, CancellationToken cancelamento);

    void Adicionar(Usuario usuario);
}
