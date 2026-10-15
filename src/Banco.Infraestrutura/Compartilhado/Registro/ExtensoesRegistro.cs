using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Banco.Infraestrutura.Compartilhado.Registro;

/// <summary>
/// Serilog com saída JSON compacta no console, enriquecido pelo contexto da requisição
/// (<c>CorrelationId</c>, <c>TraceId</c>) e com dados sensíveis mascarados (<see cref="FiltroDadosSensiveis"/>). Níveis ajustáveis pela seção <c>Serilog</c> da configuração.
/// Serve a qualquer host (<c>WebApplicationBuilder</c> das Apis e <c>HostApplicationBuilder</c> do Worker).
/// </summary>
public static class ExtensoesRegistro
{
    public static TBuilder AdicionarRegistroBanco<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var configuracaoApp = builder.Configuration;

        // preserveStaticLogger: cada host tem o seu logger (sem estado global entre hosts/testes).
        builder.Services.AddSerilog(
            (servicos, configuracao) => configuracao
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting", LogEventLevel.Information)
                .ReadFrom.Configuration(configuracaoApp)
                .ReadFrom.Services(servicos)
                .Enrich.FromLogContext()
                .Enrich.With<FiltroDadosSensiveis>() // por último: mascara o que os demais enriquecedores adicionaram
                .WriteTo.Console(new RenderedCompactJsonFormatter()),
            preserveStaticLogger: true);

        return builder;
    }
}
