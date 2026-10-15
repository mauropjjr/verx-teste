using Microsoft.AspNetCore.Http;

namespace Banco.Infraestrutura.Compartilhado.Correlacao;

public static class ExtensoesCorrelacao
{
    /// <summary>Identificador de correlação da requisição atual (para propagar a eventos/outbox).</summary>
    public static string? ObterIdCorrelacao(this HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return contexto.Items.TryGetValue(MiddlewareCorrelacao.ChaveItem, out var valor) ? valor as string : null;
    }
}
