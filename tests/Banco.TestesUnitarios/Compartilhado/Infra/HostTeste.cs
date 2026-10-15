using Banco.Infraestrutura.Compartilhado;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Banco.TestesUnitarios.Compartilhado.Infra;

/// <summary>
/// Host mínimo em memória (TestServer) com os padrões de Banco.Infraestrutura.Compartilhado aplicados — o mesmo
/// pipeline que as Apis usam, sem depender de um Program concreto.
/// </summary>
internal sealed class HostTeste : IAsyncDisposable
{
    private readonly WebApplication _app;

    private HostTeste(WebApplication app)
    {
        _app = app;
        Cliente = app.GetTestClient();
    }

    public HttpClient Cliente { get; }

    public IServiceProvider Servicos => _app.Services;

    public static async Task<HostTeste> IniciarAsync(
        Action<WebApplicationBuilder>? configurarServicos = null,
        Action<WebApplication>? mapearEndpoints = null,
        IDictionary<string, string?>? configuracao = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        if (configuracao is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuracao);
        }

        builder.AdicionarPadroesBanco();
        configurarServicos?.Invoke(builder);

        var app = builder.Build();
        app.UsarPadroesBanco();
        mapearEndpoints?.Invoke(app);

        await app.StartAsync();
        return new HostTeste(app);
    }

    public async ValueTask DisposeAsync()
    {
        Cliente.Dispose();
        await _app.DisposeAsync();
    }
}
