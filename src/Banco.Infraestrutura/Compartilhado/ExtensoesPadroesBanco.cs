using Banco.Infraestrutura.Compartilhado.Autenticacao;
using Banco.Infraestrutura.Compartilhado.Correlacao;
using Banco.Infraestrutura.Compartilhado.Erros;
using Banco.Infraestrutura.Compartilhado.LimiteTaxa;
using Banco.Infraestrutura.Compartilhado.Registro;
using Banco.Infraestrutura.Compartilhado.Saude;
using Microsoft.AspNetCore.Builder;
using Serilog;

namespace Banco.Infraestrutura.Compartilhado;

/// <summary>
/// Ponto único de composição dos padrões transversais de host (design D2/D10/D11), usado por todas as Apis:
/// <c>builder.AdicionarPadroesBanco()</c> antes do <c>Build()</c> e <c>app.UsarPadroesBanco()</c> depois.
/// </summary>
public static class ExtensoesPadroesBanco
{
    public static WebApplicationBuilder AdicionarPadroesBanco(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AdicionarRegistroBanco();
        builder.Services.AdicionarProblemDetailsBanco();
        builder.Services.AdicionarAutenticacaoBanco(builder.Configuration);
        builder.Services.AdicionarLimiteTaxaBanco(builder.Configuration);
        builder.Services.AdicionarVerificacoesSaude();
        return builder;
    }

    public static WebApplication UsarPadroesBanco(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Ordem importa: a correlação envolve tudo para que até o log da exceção carregue o id.
        app.UseMiddleware<MiddlewareCorrelacao>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSerilogRequestLogging();
        app.UseAuthentication();
        app.UseRateLimiter(); // depois da autenticação: particiona pelo sub do token
        app.UseAuthorization();

        app.MapearVerificacoesSaude();
        return app;
    }
}
