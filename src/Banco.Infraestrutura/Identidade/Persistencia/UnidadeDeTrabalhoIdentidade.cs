using Banco.Aplicacao.Identidade.Portas;

namespace Banco.Infraestrutura.Identidade.Persistencia;

/// <summary>O <c>SaveChanges</c> do <see cref="IdentidadeDbContext"/> é a transação do contexto (design D2.1).</summary>
public sealed class UnidadeDeTrabalhoIdentidade(IdentidadeDbContext contexto) : IUnidadeDeTrabalhoIdentidade
{
    public Task ConfirmarAsync(CancellationToken cancelamento) => contexto.SaveChangesAsync(cancelamento);
}
