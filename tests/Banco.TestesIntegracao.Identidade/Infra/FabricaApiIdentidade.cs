using Banco.Testes.Comum;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Banco.TestesIntegracao.Identidade.Infra;

/// <summary>
/// Sobe o <c>Banco.Api.Identidade</c> real (migração + seed no startup) contra o Postgres do Testcontainers, com
/// relógio controlável, chave RSA em pasta temporária e captura dos logs.
/// </summary>
internal sealed class FabricaApiIdentidade(PostgresIdentidade postgres) : WebApplicationFactory<Program>
{
    private readonly string _pastaChaves = Path.Combine(Path.GetTempPath(), "banco-chaves-" + Guid.NewGuid().ToString("N"));

    public RelogioAjustavel Relogio { get; } = new();

    public SinkMemoria Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Identidade", postgres.StringConexao);
        builder.UseSetting("Identidade:Chaves:CaminhoArquivo", Path.Combine(_pastaChaves, "identidade-rsa.pem"));
        // O limite por IP é coberto nos testes do Compartilhado; aqui todas as requisições vêm do mesmo "IP".
        builder.UseSetting("LimiteTaxa:LoginPorIpPorMinuto", "1000");
        builder.ConfigureTestServices(servicos =>
        {
            servicos.AddSingleton<TimeProvider>(Relogio);
            servicos.AddSingleton<ILogEventSink>(Logs);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_pastaChaves))
        {
            Directory.Delete(_pastaChaves, recursive: true);
        }
    }
}
