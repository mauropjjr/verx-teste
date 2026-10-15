using Banco.Aplicacao.Identidade.Portas;
using Banco.Dominio.Identidade;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Identidade.Persistencia;

public sealed class RepositorioUsuarios(IdentidadeDbContext contexto) : IRepositorioUsuarios
{
    public Task<Usuario?> ObterPorLoginAsync(string login, CancellationToken cancelamento)
    {
        var normalizado = Usuario.NormalizarLogin(login);
        return contexto.Usuarios.SingleOrDefaultAsync(u => u.Login == normalizado, cancelamento);
    }

    public void Adicionar(Usuario usuario) => contexto.Usuarios.Add(usuario);
}
