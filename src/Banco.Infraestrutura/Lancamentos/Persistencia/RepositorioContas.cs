using Banco.Aplicacao.Lancamentos.Portas;
using Banco.Dominio.Lancamentos;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Lancamentos.Persistencia;

public sealed class RepositorioContas(LancamentosDbContext contexto) : IRepositorioContas
{
    public Task<Conta?> ObterAsync(string id, CancellationToken cancelamento) =>
        contexto.Contas.SingleOrDefaultAsync(c => c.Id == id, cancelamento);

    public async Task<IReadOnlyList<Conta>> ListarAsync(CancellationToken cancelamento) =>
        await contexto.Contas.AsNoTracking().OrderBy(c => c.Id).ToListAsync(cancelamento);

    public void Adicionar(Conta conta) => contexto.Contas.Add(conta);
}
