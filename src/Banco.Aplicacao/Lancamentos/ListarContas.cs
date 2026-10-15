using Banco.Aplicacao.Lancamentos.Portas;
using Banco.Dominio.Lancamentos;

namespace Banco.Aplicacao.Lancamentos;

/// <summary>Caso de uso "Listar contas" (spec ledger-entries): identificador, titular mascarado e situação.</summary>
public sealed class ListarContas(IRepositorioContas contas)
{
    public async Task<IReadOnlyList<ContaResumida>> ExecutarAsync(CancellationToken cancelamento)
    {
        var todas = await contas.ListarAsync(cancelamento);
        return [.. todas.Select(c => new ContaResumida(c.Id, c.TitularMascarado, c.Situacao))];
    }
}

/// <summary>Conta como sai do serviço: nunca com o nome completo do titular.</summary>
public sealed record ContaResumida(string Id, string TitularMascarado, SituacaoConta Situacao);
