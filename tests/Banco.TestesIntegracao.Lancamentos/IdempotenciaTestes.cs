using System.Net;
using Banco.TestesIntegracao.Lancamentos.Infra;
using Microsoft.EntityFrameworkCore;
using static Banco.TestesIntegracao.Lancamentos.Infra.ExtensoesClienteLancamentos;

namespace Banco.TestesIntegracao.Lancamentos;

// Spec ledger-entries, requisito "Idempotência do registro" (design D4: UNIQUE + hash_requisicao).
[Collection(ColecaoLancamentos.Nome)]
public sealed class IdempotenciaTestes : IDisposable
{
    private readonly FabricaApiLancamentos _fabrica;
    private readonly HttpClient _operador;

    public IdempotenciaTestes(PostgresLancamentos postgres)
    {
        _fabrica = new FabricaApiLancamentos(postgres);
        _operador = _fabrica.ClienteComPapel("operador");
    }

    public void Dispose()
    {
        _operador.Dispose();
        _fabrica.Dispose();
    }

    private Task<List<decimal>> ValoresPorChaveAsync(string chave) =>
        _fabrica.ExecutarNoBancoAsync(c => c.Lancamentos.Where(l => l.ChaveIdempotencia == chave).Select(l => l.Valor).ToListAsync());

    [Fact]
    public async Task Registro_sem_Idempotency_Key_retorna_400_indicando_o_cabecalho()
    {
        using var resposta = await _operador.RegistrarAsync("CC-0002", new { tipo = "CREDITO", valor = 10m, descricao = "x" }, chave: null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.True((await resposta.LerJsonAsync()).GetProperty("errors").TryGetProperty("Idempotency-Key", out _));
    }

    [Fact]
    public async Task Reenvio_com_a_mesma_chave_repete_o_lancamento_original_e_mesma_chave_com_outro_corpo_retorna_422()
    {
        var chave = NovaChave();
        var corpo = new { tipo = "CREDITO", valor = 10.00m, descricao = "Depósito" };

        using var primeira = await _operador.RegistrarAsync("CC-0002", corpo, chave);
        // Mesmo conteúdo, escrito de outro jeito (zeros à direita, espaços): continua sendo a mesma requisição.
        using var reenvio = await _operador.RegistrarAsync("CC-0002", new { tipo = "CREDITO", valor = 10.0m, descricao = " Depósito " }, chave);
        using var outroCorpo = await _operador.RegistrarAsync("CC-0002", corpo with { valor = 20.00m }, chave);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reenvio.StatusCode);
        var original = await primeira.LerJsonAsync();
        var repetido = await reenvio.LerJsonAsync();
        Assert.Equal(original.GetProperty("id").GetGuid(), repetido.GetProperty("id").GetGuid());
        Assert.Equal(original.GetProperty("ocorridoEm").GetDateTimeOffset(), repetido.GetProperty("ocorridoEm").GetDateTimeOffset());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, outroCorpo.StatusCode);
        Assert.Equal("application/problem+json", outroCorpo.Content.Headers.ContentType?.MediaType);
        Assert.Equal([10.00m], await ValoresPorChaveAsync(chave));
    }

    [Fact]
    public async Task Vinte_requisicoes_concorrentes_com_a_mesma_chave_criam_um_unico_lancamento_e_todas_trazem_o_mesmo_id()
    {
        var chave = NovaChave();
        var corpo = new { tipo = "DEBITO", valor = 33.33m, descricao = "Pagamento" };

        var respostas = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _operador.RegistrarAsync("CC-0002", corpo, chave)));

        try
        {
            Assert.All(respostas, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            var ids = await Task.WhenAll(respostas.Select(async r => (await r.LerJsonAsync()).GetProperty("id").GetGuid()));
            Assert.Single(ids.Distinct());
            Assert.Equal([33.33m], await ValoresPorChaveAsync(chave));
        }
        finally
        {
            foreach (var resposta in respostas)
            {
                resposta.Dispose();
            }
        }
    }
}
