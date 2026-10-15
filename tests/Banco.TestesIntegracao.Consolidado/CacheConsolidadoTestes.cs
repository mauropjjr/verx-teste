using System.Diagnostics;
using System.Net;
using Banco.Infraestrutura.Consolidado.Persistencia;
using Banco.TestesIntegracao.Consolidado.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using static Banco.TestesIntegracao.Consolidado.Infra.FabricaApiConsolidado;

namespace Banco.TestesIntegracao.Consolidado;

// Design D9: cache-aside no Redis com a versão da conta na chave e fallback ao Postgres com o Redis fora;
// spec platform-operations "Verificação de saúde" (Redis parado ⇒ /health/ready 503, /health/live 200).
[Collection(ColecaoConsolidado.Nome)]
public sealed class CacheConsolidadoTestes(InfraConsolidado infra)
{
    [Fact]
    public async Task Segunda_consulta_vem_do_cache_e_a_nova_versao_da_conta_reflete_o_dado_novo()
    {
        await using var api = new FabricaApiConsolidado(infra);
        var cliente = api.ClienteComPapel("consulta");
        var conta = HostConsolidado.NovaConta();
        await api.ProjetarAsync(Credito(conta, 100.00m, "2026-03-10T09:00:00Z"));
        var rotaPeriodo = $"/api/v1/contas/{conta}/consolidado-diario?de=2026-03-10&ate=2026-03-10";
        var rotaSaldo = $"/api/v1/contas/{conta}/saldo?em=2026-03-11T12:00:00Z";

        Assert.Equal(100.00m, await FechamentoAsync(cliente, rotaPeriodo));
        Assert.Equal(100.00m, await SaldoAsync(cliente, rotaSaldo));

        // O banco muda sem a versão da conta subir: as consultas seguintes ainda vêm do cache.
        await AlterarSemVersaoAsync(api, conta, 50.00m);
        Assert.Equal(100.00m, await FechamentoAsync(cliente, rotaPeriodo));
        Assert.Equal(100.00m, await SaldoAsync(cliente, rotaSaldo));

        // O Worker incrementa versao:{conta} a cada projeção: a chave muda e o dado novo aparece.
        await using var redis = await ConnectionMultiplexer.ConnectAsync(infra.StringConexaoRedis);
        await redis.GetDatabase().StringIncrementAsync($"versao:{conta}");
        Assert.Equal(150.00m, await FechamentoAsync(cliente, rotaPeriodo));
        Assert.Equal(150.00m, await SaldoAsync(cliente, rotaSaldo));
    }

    [Fact]
    public async Task Com_o_Redis_fora_responde_pelo_Postgres_sem_esperas_longas_e_ready_responde_503()
    {
        // Porta sem servidor: o Redis está "parado" desde a subida.
        await using var api = new FabricaApiConsolidado(infra, new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" });
        var cliente = api.ClienteComPapel("consulta");
        var conta = HostConsolidado.NovaConta();
        await api.ProjetarAsync(Credito(conta, 80.00m, "2026-03-10T09:00:00Z"));

        var cronometro = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(80.00m, await FechamentoAsync(cliente, $"/api/v1/contas/{conta}/consolidado-diario?de=2026-03-10&ate=2026-03-10"));
            Assert.Equal(80.00m, await SaldoAsync(cliente, $"/api/v1/contas/{conta}/saldo?em=2026-03-11T00:00:00Z"));
        }

        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(5), $"20 consultas levaram {cronometro.Elapsed}.");

        var pronto = await cliente.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, pronto.StatusCode);
        var componentes = (await LerJsonAsync(pronto)).GetProperty("componentes");
        Assert.Equal("Unhealthy", componentes.GetProperty("redis").GetString());
        Assert.Equal("Healthy", componentes.GetProperty("postgres-consolidado").GetString());
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/health/live")).StatusCode);
    }

    private static async Task<decimal> FechamentoAsync(HttpClient cliente, string rota)
    {
        var resposta = await cliente.GetAsync(rota);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await LerJsonAsync(resposta)).GetProperty("dias")[0].GetProperty("fechamento").GetDecimal();
    }

    private static async Task<decimal> SaldoAsync(HttpClient cliente, string rota)
    {
        var resposta = await cliente.GetAsync(rota);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await LerJsonAsync(resposta)).GetProperty("saldo").GetDecimal();
    }

    private static async Task AlterarSemVersaoAsync(FabricaApiConsolidado api, string conta, decimal credito)
    {
        await using var escopo = api.Services.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ConsolidadoDbContext>();
        await contexto.Database.ExecuteSqlAsync($"UPDATE resumos_diarios SET creditos = creditos + {credito} WHERE conta_id = {conta}");
    }
}
