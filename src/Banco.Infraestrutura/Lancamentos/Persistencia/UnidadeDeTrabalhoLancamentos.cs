using Banco.Aplicacao.Lancamentos.Portas;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Banco.Infraestrutura.Lancamentos.Persistencia;

/// <summary>
/// O <c>SaveChanges</c> do <see cref="LancamentosDbContext"/> é a transação do contexto (design D2.1): tudo o
/// que foi adicionado no escopo — o lançamento e, com o outbox (D5), a sua mensagem — entra junto ou nada
/// entra. Violação das UNIQUE de idempotência/estorno (SQLSTATE 23505) volta como
/// <see cref="ResultadoConfirmacao"/>.
/// </summary>
public sealed class UnidadeDeTrabalhoLancamentos(LancamentosDbContext contexto) : IUnidadeDeTrabalhoLancamentos
{
    public async Task<ResultadoConfirmacao> ConfirmarAsync(CancellationToken cancelamento)
    {
        try
        {
            await contexto.SaveChangesAsync(cancelamento);
            return ResultadoConfirmacao.Confirmado;
        }
        catch (DbUpdateException erro) when (Traduzir(erro) is { } resultado)
        {
            // Nada foi gravado; descarta o pendente para o escopo seguir consultando o estado real.
            contexto.ChangeTracker.Clear();
            return resultado;
        }
    }

    private static ResultadoConfirmacao? Traduzir(DbUpdateException erro) =>
        erro.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violacao
            ? violacao.ConstraintName switch
            {
                LancamentosDbContext.IndiceChaveIdempotencia => ResultadoConfirmacao.ChaveIdempotenciaDuplicada,
                LancamentosDbContext.IndiceEstornoDe => ResultadoConfirmacao.EstornoDuplicado,
                _ => null,
            }
            : null;
}
