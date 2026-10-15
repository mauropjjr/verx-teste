using Banco.Dominio.Lancamentos;

namespace Banco.Aplicacao.Lancamentos.Portas;

/// <summary>
/// Lado de leitura do extrato (design D2.1: sem repositório, SQL direto). Ordem cronológica decrescente por
/// <c>(ocorrido_em, id)</c>, que também é a chave do cursor.
/// </summary>
public interface IConsultasExtrato
{
    /// <summary>
    /// Até <paramref name="quantidade"/> lançamentos da conta com <c>desde &lt;= ocorridoEm &lt; ateExclusivo</c>
    /// (limites opcionais) e estritamente depois do <paramref name="cursor"/> na ordem decrescente.
    /// </summary>
    Task<IReadOnlyList<ItemExtrato>> ListarAsync(
        string contaId,
        DateTimeOffset? desde,
        DateTimeOffset? ateExclusivo,
        CursorExtrato? cursor,
        int quantidade,
        CancellationToken cancelamento);
}

/// <summary>Linha do extrato.</summary>
public sealed record ItemExtrato(
    Guid Id,
    string ContaId,
    TipoLancamento Tipo,
    decimal Valor,
    string Descricao,
    DateTimeOffset OcorridoEm,
    Guid? EstornoDe);
