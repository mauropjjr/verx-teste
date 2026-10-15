using Banco.Dominio.Consolidado;

namespace Banco.Aplicacao.Consolidado.Portas;

/// <summary>
/// Transação explícita da projeção (design D2.1/D7): inbox, resumo diário, lançamento projetado e marca de
/// atualização são gravados juntos. Descartar sem <see cref="ITransacaoConsolidado.ConfirmarAsync"/> desfaz tudo.
/// </summary>
public interface IUnidadeDeTrabalhoConsolidado
{
    Task<ITransacaoConsolidado> IniciarAsync(CancellationToken cancelamento);
}

public interface ITransacaoConsolidado : IAsyncDisposable
{
    Task ConfirmarAsync(CancellationToken cancelamento);
}

/// <summary>Inbox (design D7): registra o id da mensagem; devolve <c>false</c> se ela já tinha sido processada.</summary>
public interface IRegistroMensagensProcessadas
{
    Task<bool> RegistrarAsync(Guid idMensagem, CancellationToken cancelamento);
}

/// <summary>Escrita da projeção do consolidado por conta (design D7) — só operações comutativas.</summary>
public interface IRepositorioResumosDiarios
{
    /// <summary>Soma os deltas ao resumo do dia (cria a linha se não existir), atomicamente no banco.</summary>
    Task SomarAsync(ResumoDiario delta, CancellationToken cancelamento);

    /// <summary>Guarda o movimento individual, usado no saldo intradiário.</summary>
    Task RegistrarMovimentoAsync(Movimento movimento, CancellationToken cancelamento);

    /// <summary>Avança <c>atualizadoAte</c> da conta para o maior <c>ocorridoEm</c> já processado.</summary>
    Task AvancarMarcaAsync(string contaId, DateTimeOffset ocorridoEm, DateTimeOffset processadoEm, CancellationToken cancelamento);
}

/// <summary>
/// Sinaliza que os dados da conta mudaram (versão no cache, design D9). Melhor esforço: a implementação não deve
/// falhar por indisponibilidade do cache — o dado já está confirmado no banco.
/// </summary>
public interface ISinalizadorVersaoConta
{
    Task IncrementarAsync(string contaId, CancellationToken cancelamento);
}
