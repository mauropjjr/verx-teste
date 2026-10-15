using Banco.Aplicacao.Lancamentos.Portas;

namespace Banco.Aplicacao.Lancamentos;

/// <summary>
/// Caso de uso "Consultar extrato de lançamentos" (spec ledger-entries): ordem cronológica decrescente, filtro
/// por dias UTC inclusivos (<c>de</c>/<c>ate</c>) e paginação por cursor. Pede um item a mais que o limite
/// para saber se há próxima página sem contar o total.
/// </summary>
public sealed class ConsultarExtrato(IRepositorioContas contas, IConsultasExtrato consultas)
{
    public const int LimitePadrao = 50;
    public const int LimiteMaximo = 100;

    /// <returns>A página, ou <c>null</c> se a conta não existe.</returns>
    public async Task<PaginaExtrato?> ExecutarAsync(ConsultaExtrato consulta, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        ArgumentOutOfRangeException.ThrowIfLessThan(consulta.Limite, 1, nameof(consulta));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(consulta.Limite, LimiteMaximo, nameof(consulta));

        if (await contas.ObterAsync(consulta.ContaId, cancelamento) is null)
        {
            return null;
        }

        var desde = consulta.De?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var ateExclusivo = consulta.Ate?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var itens = await consultas.ListarAsync(
            consulta.ContaId,
            desde is { } d ? new DateTimeOffset(d) : null,
            ateExclusivo is { } a ? new DateTimeOffset(a) : null,
            consulta.Cursor,
            consulta.Limite + 1,
            cancelamento);

        if (itens.Count <= consulta.Limite)
        {
            return new PaginaExtrato(itens, ProximoCursor: null);
        }

        var pagina = itens.Take(consulta.Limite).ToList();
        var ultimo = pagina[^1];
        return new PaginaExtrato(pagina, new CursorExtrato(ultimo.OcorridoEm, ultimo.Id).Codificar());
    }
}

/// <summary>Parâmetros do extrato, já validados quanto a formato pelo host.</summary>
public sealed record ConsultaExtrato(string ContaId, DateOnly? De, DateOnly? Ate, int Limite, CursorExtrato? Cursor);

/// <summary>Uma página do extrato e o cursor da próxima (<c>null</c> na última).</summary>
public sealed record PaginaExtrato(IReadOnlyList<ItemExtrato> Itens, string? ProximoCursor);
