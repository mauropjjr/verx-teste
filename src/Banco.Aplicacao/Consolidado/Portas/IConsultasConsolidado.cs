using Banco.Dominio.Consolidado;

namespace Banco.Aplicacao.Consolidado.Portas;

/// <summary>
/// Lado de leitura do consolidado (design D2.1: sem repositório, SQL direto; D7: fechamento por soma acumulada).
/// Conta sem nenhum lançamento processado responde como saldo zero e <c>AtualizadoAte</c> nulo.
/// </summary>
public interface IConsultasConsolidado
{
    /// <summary>Cada dia de <paramref name="de"/> a <paramref name="ate"/> (inclusive), com os dias sem movimento.</summary>
    Task<ConsolidadoPeriodo> ConsultarPeriodoAsync(string contaId, DateOnly de, DateOnly ate, CancellationToken cancelamento);

    /// <summary>Fechamento do dia anterior a <paramref name="em"/> + lançamentos do dia de <paramref name="em"/> até ele.</summary>
    Task<SaldoEmInstante> ConsultarSaldoAsync(string contaId, DateTimeOffset em, CancellationToken cancelamento);
}

/// <summary>Consolidado de um período e o instante do último lançamento processado da conta.</summary>
public sealed record ConsolidadoPeriodo(IReadOnlyList<ConsolidadoDiario> Dias, DateTimeOffset? AtualizadoAte);

/// <summary>Saldo da conta no instante <see cref="Em"/> e o instante do último lançamento processado da conta.</summary>
public sealed record SaldoEmInstante(decimal Saldo, DateTimeOffset Em, DateTimeOffset? AtualizadoAte);
