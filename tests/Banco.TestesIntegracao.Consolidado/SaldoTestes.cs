using System.Globalization;
using System.Net;
using Banco.TestesIntegracao.Consolidado.Infra;
using static Banco.TestesIntegracao.Consolidado.Infra.FabricaApiConsolidado;

namespace Banco.TestesIntegracao.Consolidado;

// Spec balance-consolidation "Consultar saldo em um instante" e "Consistência eventual explícita"; design D7.
[Collection(ColecaoConsolidado.Nome)]
public sealed class SaldoTestes(InfraConsolidado infra)
{
    private static readonly DateTimeOffset _agora = Instante("2026-03-10T16:00:00Z");

    [Fact]
    public async Task Saldo_intradiario_soma_o_fechamento_anterior_e_os_lancamentos_do_dia_ate_o_instante_e_expoe_atualizadoAte()
    {
        await using var api = new FabricaApiConsolidado(infra, relogio: new RelogioFixo(_agora));
        var cliente = api.ClienteComPapel("consulta");
        var conta = HostConsolidado.NovaConta();
        await api.ProjetarAsync(
            Credito(conta, 500.00m, "2026-03-09T11:00:00Z"), // fechamento de D-1 = 500
            Credito(conta, 50.00m, "2026-03-10T10:00:00Z"),
            Debito(conta, 20.00m, "2026-03-10T15:00:00Z"));

        var resposta = await cliente.GetAsync($"/api/v1/contas/{conta}/saldo?em=2026-03-10T12:00:00Z");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJsonAsync(resposta);
        Assert.Equal(550.00m, corpo.GetProperty("saldo").GetDecimal());
        Assert.Equal(Instante("2026-03-10T12:00:00Z"), corpo.GetProperty("em").GetDateTimeOffset());
        Assert.Equal(Instante("2026-03-10T15:00:00Z"), corpo.GetProperty("atualizadoAte").GetDateTimeOffset());

        // Sem `em`, vale o instante atual (relógio da Api): inclui o débito das 15h.
        var agora = await LerJsonAsync(await cliente.GetAsync($"/api/v1/contas/{conta}/saldo"));
        Assert.Equal(530.00m, agora.GetProperty("saldo").GetDecimal());
        Assert.Equal(_agora, agora.GetProperty("em").GetDateTimeOffset());

        // Instante inválido é recusado.
        var invalido = await cliente.GetAsync($"/api/v1/contas/{conta}/saldo?em=ontem");
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }

    private static DateTimeOffset Instante(string texto) => DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture);
}
