using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Banco.Infraestrutura.Consolidado.Mensageria;

/// <summary>
/// Decide o que vale tentar de novo (design D8): banco momentaneamente fora, timeout, deadlock/serialização
/// (<see cref="NpgsqlException.IsTransient"/>). O resto — evento inválido, erro de SQL, bug — falharia igual na
/// próxima tentativa e vai direto para a DLQ.
/// </summary>
public static class ClassificadorFalhas
{
    public static bool EhTransitoria(Exception erro) => erro switch
    {
        NpgsqlException npgsql => npgsql.IsTransient,
        TimeoutException => true,
        DbUpdateException { InnerException: { } interna } => EhTransitoria(interna),
        InvalidOperationException { InnerException: NpgsqlException { IsTransient: true } } => true,
        _ => false,
    };
}
