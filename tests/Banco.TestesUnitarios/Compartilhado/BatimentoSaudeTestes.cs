using Banco.Infraestrutura.Compartilhado.Saude;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Banco.TestesUnitarios.Compartilhado;

// Healthcheck de container para host sem HTTP (Worker): o host grava um batimento enquanto as verificações de saúde
// passam, e `--verificar-saude` checa a idade do arquivo (spec platform-operations "Saúde dos serviços"; design D14).
public sealed class BatimentoSaudeTestes : IDisposable
{
    private readonly OpcoesBatimentoSaude _opcoes = new()
    {
        Arquivo = Path.Combine(Path.GetTempPath(), $"batimento-{Guid.NewGuid():N}"),
        Intervalo = TimeSpan.FromMilliseconds(50),
        IdadeMaxima = TimeSpan.FromSeconds(30),
    };

    [Fact]
    public async Task Host_saudavel_grava_batimento_e_a_verificacao_por_linha_de_comando_sai_com_0()
    {
        using var host = await IniciarAsync(HealthCheckResult.Healthy());

        await AguardarAsync(() => File.Exists(_opcoes.Arquivo));

        Assert.Equal(0, VerificacaoSaudeLinhaComando.ExecutarBatimentoSeSolicitado(["--verificar-saude"], _opcoes));
        await host.StopAsync();
    }

    [Fact]
    public async Task Host_com_dependencia_fora_nao_grava_batimento_e_a_verificacao_sai_com_1()
    {
        using var host = await IniciarAsync(HealthCheckResult.Unhealthy());

        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Assert.False(File.Exists(_opcoes.Arquivo));
        Assert.Equal(1, VerificacaoSaudeLinhaComando.ExecutarBatimentoSeSolicitado(["--verificar-saude"], _opcoes));
        await host.StopAsync();
    }

    [Fact]
    public void Batimento_mais_velho_que_a_idade_maxima_sai_com_1_e_sem_o_argumento_nao_verifica()
    {
        File.WriteAllText(_opcoes.Arquivo, "x");
        File.SetLastWriteTimeUtc(_opcoes.Arquivo, DateTime.UtcNow.AddMinutes(-5));

        Assert.Equal(1, VerificacaoSaudeLinhaComando.ExecutarBatimentoSeSolicitado(["--verificar-saude"], _opcoes));
        Assert.Null(VerificacaoSaudeLinhaComando.ExecutarBatimentoSeSolicitado([], _opcoes));
    }

    public void Dispose() => File.Delete(_opcoes.Arquivo);

    private async Task<IHost> IniciarAsync(HealthCheckResult estado)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddHealthChecks().AddCheck("dependencia", () => estado);
        builder.Services.AdicionarBatimentoSaude(_opcoes);
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static async Task AguardarAsync(Func<bool> condicao)
    {
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (!condicao())
        {
            Assert.True(DateTime.UtcNow < limite, "Condição não atingida a tempo.");
            await Task.Delay(20);
        }
    }
}
