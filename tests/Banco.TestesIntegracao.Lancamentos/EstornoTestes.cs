using System.Net;
using Banco.TestesIntegracao.Lancamentos.Infra;
using static Banco.TestesIntegracao.Lancamentos.Infra.ExtensoesClienteLancamentos;

namespace Banco.TestesIntegracao.Lancamentos;

// Spec ledger-entries, requisito "Estorno de lançamento" (+ idempotência do estorno).
[Collection(ColecaoLancamentos.Nome)]
public sealed class EstornoTestes : IDisposable
{
    private readonly FabricaApiLancamentos _fabrica;
    private readonly HttpClient _operador;

    public EstornoTestes(PostgresLancamentos postgres)
    {
        _fabrica = new FabricaApiLancamentos(postgres);
        _operador = _fabrica.ClienteComPapel("operador");
    }

    public void Dispose()
    {
        _operador.Dispose();
        _fabrica.Dispose();
    }

    [Fact]
    public async Task Estorno_de_um_credito_cria_debito_de_mesmo_valor_com_estornoDe_e_segundo_estorno_retorna_409()
    {
        using var registro = await _operador.RegistrarAsync("CC-0003", new { tipo = "CREDITO", valor = 100.00m, descricao = "Depósito" }, NovaChave());
        var e1 = (await registro.LerJsonAsync()).GetProperty("id").GetGuid();
        var chaveEstorno = NovaChave();

        using var estorno = await _operador.EstornarAsync("CC-0003", e1, chaveEstorno);

        Assert.Equal(HttpStatusCode.Created, estorno.StatusCode);
        var corpo = await estorno.LerJsonAsync();
        Assert.Equal("DEBITO", corpo.GetProperty("tipo").GetString());
        Assert.Equal(100.00m, corpo.GetProperty("valor").GetDecimal());
        Assert.Equal(e1, corpo.GetProperty("estornoDe").GetGuid());
        Assert.Equal("CC-0003", corpo.GetProperty("contaId").GetString());
        Assert.NotEqual(e1, corpo.GetProperty("id").GetGuid());

        // Reenvio do mesmo estorno (mesma chave) repete o resultado; outro estorno do mesmo lançamento é conflito.
        using var reenvio = await _operador.EstornarAsync("CC-0003", e1, chaveEstorno);
        Assert.Equal(HttpStatusCode.Created, reenvio.StatusCode);
        Assert.Equal(corpo.GetProperty("id").GetGuid(), (await reenvio.LerJsonAsync()).GetProperty("id").GetGuid());

        using var segundo = await _operador.EstornarAsync("CC-0003", e1, NovaChave());
        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        Assert.Equal("application/problem+json", segundo.Content.Headers.ContentType?.MediaType);

        // Estorno não se estorna.
        using var estornoDoEstorno = await _operador.EstornarAsync("CC-0003", corpo.GetProperty("id").GetGuid(), NovaChave());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, estornoDoEstorno.StatusCode);
    }

    [Fact]
    public async Task Estorno_de_lancamento_inexistente_ou_de_outra_conta_retorna_404_sem_chave_400_e_consulta_403()
    {
        using var registro = await _operador.RegistrarAsync("CC-0003", new { tipo = "DEBITO", valor = 5m, descricao = "Tarifa" }, NovaChave());
        var id = (await registro.LerJsonAsync()).GetProperty("id").GetGuid();

        using var inexistente = await _operador.EstornarAsync("CC-0003", Guid.CreateVersion7(), NovaChave());
        using var deOutraConta = await _operador.EstornarAsync("CC-0004", id, NovaChave());
        using var contaInexistente = await _operador.EstornarAsync("CC-9999", id, NovaChave());
        using var semChave = await _operador.EstornarAsync("CC-0003", id, chave: null);
        using var consulta = _fabrica.ClienteComPapel("consulta");
        using var comoConsulta = await consulta.EstornarAsync("CC-0003", id, NovaChave());

        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deOutraConta.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, contaInexistente.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semChave.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comoConsulta.StatusCode);
    }
}
