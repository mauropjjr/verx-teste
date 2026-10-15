namespace Banco.Dominio.Consolidado;

/// <summary>
/// Deltas de um dia (UTC) de uma conta: total de créditos e de débitos. É o que se guarda por dia (design D7) —
/// somas comutativas, então a ordem em que os movimentos chegam não muda o resultado; o fechamento acumulado é
/// derivado na leitura (<see cref="CalculadoraSaldo"/>).
/// </summary>
public sealed record ResumoDiario(string ContaId, DateOnly Dia, decimal Creditos, decimal Debitos)
{
    public decimal SaldoDoDia => Creditos - Debitos;

    /// <summary>Contribuição de um único movimento ao resumo do seu dia.</summary>
    public static ResumoDiario DoMovimento(Movimento movimento)
    {
        ArgumentNullException.ThrowIfNull(movimento);
        return movimento.Natureza == NaturezaMovimento.Credito
            ? new ResumoDiario(movimento.ContaId, movimento.Dia, movimento.Valor, 0m)
            : new ResumoDiario(movimento.ContaId, movimento.Dia, 0m, movimento.Valor);
    }

    /// <summary>Soma dois resumos da mesma conta e dia.</summary>
    public ResumoDiario Somar(ResumoDiario outro)
    {
        ArgumentNullException.ThrowIfNull(outro);
        if (outro.ContaId != ContaId || outro.Dia != Dia)
        {
            throw new ArgumentException("Só é possível somar resumos da mesma conta e do mesmo dia.", nameof(outro));
        }

        return this with { Creditos = Creditos + outro.Creditos, Debitos = Debitos + outro.Debitos };
    }
}

/// <summary>Um dia do consolidado por período: deltas do dia e saldo de fechamento acumulado.</summary>
public sealed record ConsolidadoDiario(DateOnly Dia, decimal Creditos, decimal Debitos, decimal SaldoDoDia, decimal Fechamento);
