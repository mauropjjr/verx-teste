using System.Net;
using Banco.TestesIntegracao.Consolidado.Infra;
using static Banco.TestesIntegracao.Consolidado.Infra.FabricaApiConsolidado;

namespace Banco.TestesIntegracao.Consolidado;

// Spec balance-consolidation "Consultar consolidado diário" (consulta por período, intervalo inválido); design D7.
[Collection(ColecaoConsolidado.Nome)]
public sealed class ConsolidadoDiarioTestes(InfraConsolidado infra)
{
    [Fact]
    public async Task Periodo_retorna_todos_os_dias_e_o_dia_sem_movimento_herda_o_fechamento_do_anterior()
    {
        await using var api = new FabricaApiConsolidado(infra);
        var conta = HostConsolidado.NovaConta();
        await api.ProjetarAsync(
            Credito(conta, 500.00m, "2026-03-05T08:00:00Z"), // antes do período: entra no fechamento anterior
            Credito(conta, 100.00m, "2026-03-10T09:00:00Z"),
            Debito(conta, 30.00m, "2026-03-10T18:00:00Z"),
            Credito(conta, 10.00m, "2026-03-12T12:00:00Z"),
            Credito(conta, 999.00m, "2026-03-13T00:00:00Z")); // depois do período: não aparece

        var resposta = await api.ClienteComPapel("consulta")
            .GetAsync($"/api/v1/contas/{conta}/consolidado-diario?de=2026-03-10&ate=2026-03-12");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJsonAsync(resposta);
        var dias = corpo.GetProperty("dias").EnumerateArray()
            .Select(d => (
                d.GetProperty("dia").GetString(),
                d.GetProperty("creditos").GetDecimal(),
                d.GetProperty("debitos").GetDecimal(),
                d.GetProperty("saldoDia").GetDecimal(),
                d.GetProperty("fechamento").GetDecimal()))
            .ToList();
        Assert.Equal(
            [
                ("2026-03-10", 100.00m, 30.00m, 70.00m, 570.00m),
                ("2026-03-11", 0m, 0m, 0m, 570.00m),
                ("2026-03-12", 10.00m, 0m, 10.00m, 580.00m),
            ],
            dias);
        Assert.Equal(DateTimeOffset.Parse("2026-03-13T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), corpo.GetProperty("atualizadoAte").GetDateTimeOffset());
    }

    [Fact]
    public async Task Intervalo_invertido_maior_que_366_dias_ou_data_invalida_responde_400_em_Problem_Details()
    {
        await using var api = new FabricaApiConsolidado(infra);
        var cliente = api.ClienteComPapel("consulta");
        var conta = HostConsolidado.NovaConta();

        foreach (var query in new[] { "de=2026-03-12&ate=2026-03-10", "de=2025-01-01&ate=2026-01-02", "de=2026-13-01&ate=2026-12-31", "ate=2026-01-01" })
        {
            var resposta = await cliente.GetAsync($"/api/v1/contas/{conta}/consolidado-diario?{query}");

            Assert.True(resposta.StatusCode == HttpStatusCode.BadRequest, $"{query}: {resposta.StatusCode}");
            Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        }

        // Limite: exatamente 366 dias é aceito.
        var limite = await cliente.GetAsync($"/api/v1/contas/{conta}/consolidado-diario?de=2025-01-01&ate=2026-01-01");
        Assert.Equal(HttpStatusCode.OK, limite.StatusCode);
        Assert.Equal(366, (await LerJsonAsync(limite)).GetProperty("dias").GetArrayLength());
    }
}
