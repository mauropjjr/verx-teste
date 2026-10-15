using Banco.Infraestrutura.Compartilhado.Correlacao;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.Infraestrutura.Compartilhado.Erros;

/// <summary>
/// Problem Details padronizado (D11): toda resposta de erro leva <c>traceId</c> e <c>idCorrelacao</c>;
/// exceções não tratadas viram 500 genérico, sem mensagem nem stack trace.
/// </summary>
public static class ExtensoesProblemDetails
{
    public static IServiceCollection AdicionarProblemDetailsBanco(this IServiceCollection servicos) =>
        servicos.AddProblemDetails(opcoes => opcoes.CustomizeProblemDetails = contexto =>
        {
            contexto.ProblemDetails.Extensions["traceId"] = MiddlewareCorrelacao.ObterIdRastreio(contexto.HttpContext);
            contexto.ProblemDetails.Extensions["idCorrelacao"] = contexto.HttpContext.ObterIdCorrelacao();
            // Nunca expor detalhes internos de exceção ao cliente.
            if (contexto.Exception is not null)
            {
                contexto.ProblemDetails.Detail = null;
                contexto.ProblemDetails.Extensions.Remove("exception");
            }
        });
}
