using Banco.Aplicacao.Consolidado.Portas;
using Banco.Dominio.Consolidado;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Consolidado.Persistencia;

/// <summary>
/// Consultas do consolidado por SQL direto (design D2.1, lado de leitura; D7), uma ida ao banco por consulta:
/// <list type="bullet">
/// <item>período: soma dos deltas anteriores a <c>de</c> + <i>window function</i> acumulando os dias do intervalo,
/// gerados por <c>generate_series</c> para incluir os dias sem movimento (servida pela PK <c>(conta_id, dia)</c>);</item>
/// <item>saldo em <c>em</c>: deltas dos dias anteriores ao de <c>em</c> + lançamentos projetados do dia até <c>em</c>
/// (índice <c>(conta_id, ocorrido_em)</c>).</item>
/// </list>
/// <c>atualizadoAte</c> vem de <c>marcas_atualizacao</c>.
/// </summary>
public sealed class ConsultasConsolidado(ConsolidadoDbContext contexto) : IConsultasConsolidado
{
    public async Task<ConsolidadoPeriodo> ConsultarPeriodoAsync(string contaId, DateOnly de, DateOnly ate, CancellationToken cancelamento)
    {
        var linhas = await contexto.Database.SqlQuery<LinhaPeriodo>($"""
            WITH anterior AS (
                SELECT COALESCE(SUM(creditos - debitos), 0) AS fechamento
                FROM resumos_diarios
                WHERE conta_id = {contaId} AND dia < {de}
            ),
            dias AS (
                SELECT {de}::date + deslocamento AS dia
                FROM generate_series(0, {ate}::date - {de}::date) AS deslocamento
            )
            SELECT d.dia AS "Dia",
                   COALESCE(r.creditos, 0) AS "Creditos",
                   COALESCE(r.debitos, 0) AS "Debitos",
                   a.fechamento + SUM(COALESCE(r.creditos - r.debitos, 0))
                       OVER (ORDER BY d.dia ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS "Fechamento",
                   (SELECT m.ultimo_ocorrido_em FROM marcas_atualizacao m WHERE m.conta_id = {contaId}) AS "AtualizadoAte"
            FROM dias d
            CROSS JOIN anterior a
            LEFT JOIN resumos_diarios r ON r.conta_id = {contaId} AND r.dia = d.dia
            ORDER BY d.dia
            """).ToListAsync(cancelamento);

        return new ConsolidadoPeriodo(
            [.. linhas.Select(l => new ConsolidadoDiario(l.Dia, l.Creditos, l.Debitos, l.Creditos - l.Debitos, l.Fechamento))],
            linhas.Count > 0 ? linhas[0].AtualizadoAte : null);
    }

    public async Task<SaldoEmInstante> ConsultarSaldoAsync(string contaId, DateTimeOffset em, CancellationToken cancelamento)
    {
        var emUtc = em.ToUniversalTime();
        var dia = DateOnly.FromDateTime(emUtc.UtcDateTime);
        var inicioDoDia = new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var linha = await contexto.Database.SqlQuery<LinhaSaldo>($"""
            SELECT (SELECT COALESCE(SUM(creditos - debitos), 0)
                    FROM resumos_diarios
                    WHERE conta_id = {contaId} AND dia < {dia})
                 + (SELECT COALESCE(SUM(CASE WHEN tipo = {LancamentoProjetado.Credito} THEN valor ELSE -valor END), 0)
                    FROM lancamentos_projetados
                    WHERE conta_id = {contaId} AND ocorrido_em >= {inicioDoDia} AND ocorrido_em <= {emUtc}) AS "Saldo",
                   (SELECT m.ultimo_ocorrido_em FROM marcas_atualizacao m WHERE m.conta_id = {contaId}) AS "AtualizadoAte"
            """).SingleAsync(cancelamento);

        return new SaldoEmInstante(linha.Saldo, emUtc, linha.AtualizadoAte);
    }

    private sealed class LinhaPeriodo
    {
        public DateOnly Dia { get; set; }

        public decimal Creditos { get; set; }

        public decimal Debitos { get; set; }

        public decimal Fechamento { get; set; }

        public DateTimeOffset? AtualizadoAte { get; set; }
    }

    private sealed class LinhaSaldo
    {
        public decimal Saldo { get; set; }

        public DateTimeOffset? AtualizadoAte { get; set; }
    }
}
