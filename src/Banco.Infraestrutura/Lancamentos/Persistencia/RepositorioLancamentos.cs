using Banco.Aplicacao.Lancamentos.Portas;
using Banco.Dominio.Lancamentos;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Lancamentos.Persistencia;

public sealed class RepositorioLancamentos(LancamentosDbContext contexto) : IRepositorioLancamentos
{
    public Task<Lancamento?> ObterAsync(string contaId, Guid id, CancellationToken cancelamento) =>
        contexto.Lancamentos.SingleOrDefaultAsync(l => l.Id == id && l.ContaId == contaId, cancelamento);

    public Task<Lancamento?> ObterPorChaveIdempotenciaAsync(string contaId, string chaveIdempotencia, CancellationToken cancelamento) =>
        contexto.Lancamentos.AsNoTracking()
            .SingleOrDefaultAsync(l => l.ContaId == contaId && l.ChaveIdempotencia == chaveIdempotencia, cancelamento);

    public Task<bool> PossuiEstornoAsync(Guid id, CancellationToken cancelamento) =>
        contexto.Lancamentos.AnyAsync(l => l.EstornoDe == id, cancelamento);

    public void Adicionar(Lancamento lancamento) => contexto.Lancamentos.Add(lancamento);
}
