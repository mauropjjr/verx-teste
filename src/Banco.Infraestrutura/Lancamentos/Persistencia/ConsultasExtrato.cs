using Banco.Aplicacao.Lancamentos;
using Banco.Aplicacao.Lancamentos.Portas;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Lancamentos.Persistencia;

/// <summary>
/// Extrato por SQL direto (design D2.1, lado de leitura): paginação por <i>keyset</i> com comparação de linha
/// <c>(ocorrido_em, id) &lt; (cursor)</c>, servida pelo índice <c>(conta_id, ocorrido_em desc, id desc)</c> —
/// custo constante por página, sem <c>OFFSET</c>, sem repetir nem omitir itens entre páginas.
/// </summary>
public sealed class ConsultasExtrato(LancamentosDbContext contexto) : IConsultasExtrato
{
    public async Task<IReadOnlyList<ItemExtrato>> ListarAsync(
        string contaId,
        DateTimeOffset? desde,
        DateTimeOffset? ateExclusivo,
        CursorExtrato? cursor,
        int quantidade,
        CancellationToken cancelamento)
    {
        // Limites ausentes viram -infinity/infinity no timestamptz (conversão padrão do Npgsql).
        var inicio = desde ?? DateTimeOffset.MinValue;
        var fim = ateExclusivo ?? DateTimeOffset.MaxValue;
        var cursorOcorridoEm = cursor?.OcorridoEm ?? DateTimeOffset.MaxValue;
        var cursorId = cursor?.Id ?? Guid.AllBitsSet;

        var linhas = await contexto.Database.SqlQuery<LinhaExtrato>($"""
            SELECT id AS "Id", conta_id AS "ContaId", tipo AS "Tipo", valor AS "Valor", descricao AS "Descricao",
                   ocorrido_em AS "OcorridoEm", estorno_de AS "EstornoDe"
            FROM lancamentos
            WHERE conta_id = {contaId}
              AND ocorrido_em >= {inicio} AND ocorrido_em < {fim}
              AND (ocorrido_em, id) < ({cursorOcorridoEm}, {cursorId})
            ORDER BY ocorrido_em DESC, id DESC
            LIMIT {quantidade}
            """).ToListAsync(cancelamento);

        return [.. linhas.Select(l => new ItemExtrato(
            l.Id, l.ContaId, ConversoesLancamentos.ParaTipo(l.Tipo), l.Valor, l.Descricao, l.OcorridoEm, l.EstornoDe))];
    }

    private sealed class LinhaExtrato
    {
        public Guid Id { get; set; }

        public string ContaId { get; set; } = "";

        public string Tipo { get; set; } = "";

        public decimal Valor { get; set; }

        public string Descricao { get; set; } = "";

        public DateTimeOffset OcorridoEm { get; set; }

        public Guid? EstornoDe { get; set; }
    }
}
