using System.Net;
using System.Text.Json;
using Banco.TestesIntegracao.Lancamentos.Infra;

namespace Banco.TestesIntegracao.Lancamentos;

// Spec ledger-entries, requisito "Listar contas" (contas de demonstração do seed).
[Collection(ColecaoLancamentos.Nome)]
public sealed class ContasTestes(PostgresLancamentos postgres) : IDisposable
{
    private readonly FabricaApiLancamentos _fabrica = new(postgres);

    public void Dispose() => _fabrica.Dispose();

    [Fact]
    public async Task Listar_contas_devolve_as_contas_demo_com_titular_mascarado_e_exige_token()
    {
        using var anonimo = _fabrica.CreateClient();
        using var semToken = await anonimo.GetAsync("/api/v1/contas");
        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);

        using var cliente = _fabrica.ClienteComPapel("consulta");
        using var resposta = await cliente.GetAsync("/api/v1/contas");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var texto = await resposta.Content.ReadAsStringAsync();
        using var corpo = JsonDocument.Parse(texto);
        var contas = corpo.RootElement.EnumerateArray().ToDictionary(c => c.GetProperty("id").GetString()!);
        Assert.All(Enumerable.Range(1, 5), i => Assert.Contains($"CC-000{i}", contas.Keys));
        Assert.Equal("Mar*** S***", contas["CC-0001"].GetProperty("titular").GetString());
        Assert.Equal("ATIVA", contas["CC-0001"].GetProperty("situacao").GetString());
        Assert.DoesNotContain("Maria Silva", texto, StringComparison.Ordinal);

        // Readiness inclui o banco de lançamentos.
        using var pronto = await anonimo.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, pronto.StatusCode);
    }
}
