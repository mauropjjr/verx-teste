using System.Text;
using System.Text.Json;
using Banco.Aplicacao.Compartilhado.Contratos;
using Banco.Infraestrutura.Compartilhado.Registro;
using Banco.Infraestrutura.Consolidado;
using Banco.Infraestrutura.Consolidado.Inicializacao;
using Banco.Infraestrutura.Consolidado.Persistencia;
using Banco.Testes.Comum;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Core;

namespace Banco.TestesIntegracao.Consolidado.Infra;

/// <summary>
/// Host genérico com a mesma composição do <c>Banco.Worker.Consolidado</c> (registro, infraestrutura do Consolidado e
/// consumidor), apontando para os containers de <see cref="InfraConsolidado"/>. O consumidor só roda depois de
/// <see cref="IniciarAsync"/>; sem isso, o teste usa os serviços (ex.: o processador) diretamente.
/// </summary>
internal sealed class HostConsolidado : IAsyncDisposable
{
    private HostConsolidado(IHost host, SinkMemoria logs)
    {
        Host = host;
        Logs = logs;
    }

    public IHost Host { get; }

    public SinkMemoria Logs { get; }

    public IServiceProvider Servicos => Host.Services;

    public static async Task<HostConsolidado> CriarAsync(
        InfraConsolidado infra,
        Action<IServiceCollection>? ajustarServicos = null,
        IDictionary<string, string?>? configuracaoExtra = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Testes",
            DisableDefaults = true,
        });
        builder.Configuration.AddInMemoryCollection(infra.Configuracao());
        if (configuracaoExtra is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuracaoExtra);
        }

        var logs = new SinkMemoria();
        builder.AdicionarRegistroBanco();
        builder.Services.AddSingleton<ILogEventSink>(logs);
        builder.Services.AdicionarInfraestruturaConsolidado(builder.Configuration);
        builder.Services.AdicionarConsumidorConsolidado(builder.Configuration);

        ajustarServicos?.Invoke(builder.Services);

        var host = builder.Build();
        await host.Services.InicializarBancoConsolidadoAsync();
        return new HostConsolidado(host, logs);
    }

    public Task IniciarAsync() => Host.StartAsync();

    public async Task<T> NoBancoAsync<T>(Func<ConsolidadoDbContext, Task<T>> consulta)
    {
        await using var escopo = Servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<ConsolidadoDbContext>());
    }

    public static byte[] Serializar(LancamentoRegistradoV1 evento) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evento, LancamentoRegistradoV1.OpcoesJson));

    public static string NovaConta() => $"CT-{Guid.NewGuid():N}"[..20];

    public async ValueTask DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
    }
}
