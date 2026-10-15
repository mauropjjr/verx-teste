using Banco.Aplicacao.Consolidado.Portas;

namespace Banco.Aplicacao.Consolidado;

/// <summary>
/// Caso de uso "Consultar consolidado diário" (spec balance-consolidation): todos os dias do intervalo, inclusive os
/// sem movimento, para no máximo <see cref="DiasMaximos"/> dias.
/// </summary>
public sealed class ConsultarConsolidadoDiario(IConsultasConsolidado consultas)
{
    public const int DiasMaximos = 366;

    /// <summary>O intervalo <paramref name="de"/>..<paramref name="ate"/> (inclusive) é aceito pela consulta.</summary>
    public static bool IntervaloValido(DateOnly de, DateOnly ate) =>
        de <= ate && ate.DayNumber - de.DayNumber + 1 <= DiasMaximos;

    public Task<ConsolidadoPeriodo> ExecutarAsync(string contaId, DateOnly de, DateOnly ate, CancellationToken cancelamento)
    {
        if (!IntervaloValido(de, ate))
        {
            throw new ArgumentException($"Intervalo inválido: o início deve ser anterior ou igual ao fim, com até {DiasMaximos} dias.", nameof(de));
        }

        return consultas.ConsultarPeriodoAsync(contaId, de, ate, cancelamento);
    }
}

/// <summary>
/// Caso de uso "Consultar saldo em um instante" (spec balance-consolidation): sem <c>em</c>, usa o instante atual
/// truncado ao segundo — o que deixa consultas próximas reaproveitarem o cache (design D9).
/// </summary>
public sealed class ConsultarSaldo(IConsultasConsolidado consultas, TimeProvider relogio)
{
    public Task<SaldoEmInstante> ExecutarAsync(string contaId, DateTimeOffset? em, CancellationToken cancelamento)
    {
        var instante = em?.ToUniversalTime() ?? AgoraAoSegundo();
        return consultas.ConsultarSaldoAsync(contaId, instante, cancelamento);
    }

    private DateTimeOffset AgoraAoSegundo()
    {
        var agora = relogio.GetUtcNow();
        return agora.AddTicks(-(agora.Ticks % TimeSpan.TicksPerSecond));
    }
}
