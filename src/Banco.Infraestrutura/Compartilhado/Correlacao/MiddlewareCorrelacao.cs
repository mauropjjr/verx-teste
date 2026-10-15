using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Banco.Infraestrutura.Compartilhado.Correlacao;

/// <summary>
/// Aceita o cabeçalho <c>X-Correlation-Id</c> quando ele tem formato seguro (ou gera um novo), devolve-o na
/// resposta e o coloca, junto com o <c>TraceId</c>, no contexto de todos os logs da requisição
/// (spec platform-operations "Rastreabilidade por correlação").
/// </summary>
public sealed partial class MiddlewareCorrelacao(RequestDelegate proximo)
{
    public const string Cabecalho = "X-Correlation-Id";
    internal const string ChaveItem = "Banco.IdCorrelacao";

    public async Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var recebido = contexto.Request.Headers[Cabecalho].ToString();
        var idCorrelacao = FormatoValido().IsMatch(recebido) ? recebido : Guid.NewGuid().ToString("N");

        contexto.Items[ChaveItem] = idCorrelacao;
        contexto.Response.OnStarting(() =>
        {
            contexto.Response.Headers[Cabecalho] = idCorrelacao;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", idCorrelacao))
        using (LogContext.PushProperty("TraceId", ObterIdRastreio(contexto)))
        {
            await proximo(contexto);
        }
    }

    /// <summary>Mesmo identificador devolvido como <c>traceId</c> no Problem Details.</summary>
    internal static string ObterIdRastreio(HttpContext contexto) => Activity.Current?.Id ?? contexto.TraceIdentifier;

    // Só caracteres seguros e tamanho limitado: evita injeção em logs/cabeçalhos.
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex FormatoValido();
}
