namespace Banco.Dominio.Consolidado;

/// <summary>
/// Regras de saldo do Consolidado (spec balance-consolidation, design D7):
/// <list type="bullet">
/// <item>deltas diários: créditos e débitos somados por dia UTC;</item>
/// <item>fechamento de D = fechamento anterior ao período + soma dos saldos dos dias até D; dia sem movimento
/// herda o fechamento do dia anterior;</item>
/// <item>saldo em um instante = fechamento do dia anterior + lançamentos do próprio dia até o instante (inclusive).</item>
/// </list>
/// </summary>
public static class CalculadoraSaldo
{
    /// <summary>Agrega movimentos em um resumo por conta e dia, em ordem de dia.</summary>
    public static IReadOnlyList<ResumoDiario> AgregarPorDia(IEnumerable<Movimento> movimentos)
    {
        ArgumentNullException.ThrowIfNull(movimentos);
        return movimentos
            .Select(ResumoDiario.DoMovimento)
            .GroupBy(r => (r.ContaId, r.Dia))
            .Select(g => g.Aggregate((acumulado, resumo) => acumulado.Somar(resumo)))
            .OrderBy(r => r.Dia)
            .ToList();
    }

    /// <summary>
    /// Consolidado de cada dia de <paramref name="de"/> a <paramref name="ate"/> (inclusive), partindo de
    /// <paramref name="fechamentoAnterior"/> (fechamento do dia anterior a <paramref name="de"/>). Resumos fora do
    /// intervalo são ignorados.
    /// </summary>
    public static IReadOnlyList<ConsolidadoDiario> ConsolidarPeriodo(
        decimal fechamentoAnterior,
        IEnumerable<ResumoDiario> resumos,
        DateOnly de,
        DateOnly ate)
    {
        ArgumentNullException.ThrowIfNull(resumos);
        if (de > ate)
        {
            throw new ArgumentException("O início do período não pode ser posterior ao fim.", nameof(de));
        }

        var porDia = resumos
            .Where(r => r.Dia >= de && r.Dia <= ate)
            .GroupBy(r => r.Dia)
            .ToDictionary(g => g.Key, g => (Creditos: g.Sum(r => r.Creditos), Debitos: g.Sum(r => r.Debitos)));

        var dias = new List<ConsolidadoDiario>(ate.DayNumber - de.DayNumber + 1);
        var fechamento = fechamentoAnterior;
        for (var dia = de; dia <= ate; dia = dia.AddDays(1))
        {
            var (creditos, debitos) = porDia.GetValueOrDefault(dia);
            var saldoDoDia = creditos - debitos;
            fechamento += saldoDoDia;
            dias.Add(new ConsolidadoDiario(dia, creditos, debitos, saldoDoDia, fechamento));
        }

        return dias;
    }

    /// <summary>
    /// Saldo no instante <paramref name="em"/>: <paramref name="fechamentoDiaAnterior"/> mais os movimentos do dia de
    /// <paramref name="em"/> ocorridos até ele (inclusive).
    /// </summary>
    public static decimal SaldoEm(decimal fechamentoDiaAnterior, IEnumerable<Movimento> movimentosDoDia, DateTimeOffset em)
    {
        ArgumentNullException.ThrowIfNull(movimentosDoDia);
        var dia = DateOnly.FromDateTime(em.UtcDateTime);
        return fechamentoDiaAnterior + movimentosDoDia
            .Where(m => m.Dia == dia && m.OcorridoEm <= em)
            .Sum(m => m.Liquido);
    }
}
