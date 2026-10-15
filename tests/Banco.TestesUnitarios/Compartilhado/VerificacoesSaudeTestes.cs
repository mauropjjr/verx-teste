using System.Net;
using Banco.TestesUnitarios.Compartilhado.Infra;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Banco.TestesUnitarios.Compartilhado;

public class VerificacoesSaudeTestes
{
    [Fact]
    public async Task Com_dependencia_fora_ready_responde_503_com_o_componente_degradado_e_live_responde_200()
    {
        await using var host = await HostTeste.IniciarAsync(configurarServicos: b =>
            b.Services.AddHealthChecks()
                .AddCheck("redis", () => HealthCheckResult.Unhealthy("conexão recusada em 10.0.0.5:6379")));

        var live = await host.Cliente.GetAsync(new Uri("/health/live", UriKind.Relative));
        var ready = await host.Cliente.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var corpoReady = await ready.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Contains("\"redis\":\"Unhealthy\"", corpoReady, StringComparison.Ordinal);
        // Sem detalhes internos (descrição/endereços) na resposta.
        Assert.DoesNotContain("10.0.0.5", corpoReady, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Com_dependencias_saudaveis_ready_responde_200()
    {
        await using var host = await HostTeste.IniciarAsync(configurarServicos: b =>
            b.Services.AddHealthChecks().AddCheck("redis", () => HealthCheckResult.Healthy()));

        var ready = await host.Cliente.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }
}
