using System.Net;
using Banco.Dominio.Lancamentos;
using Banco.TestesIntegracao.Lancamentos.Infra;
using Microsoft.EntityFrameworkCore;
using static Banco.TestesIntegracao.Lancamentos.Infra.ExtensoesClienteLancamentos;

namespace Banco.TestesIntegracao.Lancamentos;

// Spec ledger-entries, requisito "Registrar lançamento"; access-control "Autorização por papéis" e
// "Validação de entrada" (descrição acima do limite).
[Collection(ColecaoLancamentos.Nome)]
public sealed class RegistroLancamentoTestes : IDisposable
{
    private readonly FabricaApiLancamentos _fabrica;
    private readonly HttpClient _operador;

    public RegistroLancamentoTestes(PostgresLancamentos postgres)
    {
        _fabrica = new FabricaApiLancamentos(postgres);
        _operador = _fabrica.ClienteComPapel("operador");
    }

    public void Dispose()
    {
        _operador.Dispose();
        _fabrica.Dispose();
    }

    private Task<int> ContarPorChaveAsync(string chave) =>
        _fabrica.ExecutarNoBancoAsync(c => c.Lancamentos.CountAsync(l => l.ChaveIdempotencia == chave));

    [Fact]
    public async Task Credito_registrado_com_sucesso_retorna_201_com_id_contaId_tipo_valor_descricao_e_ocorridoEm()
    {
        var antes = DateTimeOffset.UtcNow;

        using var resposta = await _operador.RegistrarAsync(
            "CC-0001", new { tipo = "CREDITO", valor = 150.75m, descricao = "Depósito em dinheiro" }, NovaChave());

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = await resposta.LerJsonAsync();
        Assert.Equal(7, corpo.GetProperty("id").GetGuid().Version);
        Assert.Equal("CC-0001", corpo.GetProperty("contaId").GetString());
        Assert.Equal("CREDITO", corpo.GetProperty("tipo").GetString());
        Assert.Equal(150.75m, corpo.GetProperty("valor").GetDecimal());
        Assert.Equal("Depósito em dinheiro", corpo.GetProperty("descricao").GetString());
        Assert.InRange(corpo.GetProperty("ocorridoEm").GetDateTimeOffset(), antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(TimeSpan.Zero, corpo.GetProperty("ocorridoEm").GetDateTimeOffset().Offset);

        // ... e passa a constar no extrato da conta, com o mesmo instante devolvido no 201.
        using var consulta = _fabrica.ClienteComPapel("consulta");
        using var extrato = await consulta.GetAsync("/api/v1/contas/CC-0001/lancamentos?limite=1");
        var primeiro = (await extrato.LerJsonAsync()).GetProperty("itens")[0];
        Assert.Equal(corpo.GetProperty("id").GetGuid(), primeiro.GetProperty("id").GetGuid());
        Assert.Equal(150.75m, primeiro.GetProperty("valor").GetDecimal());
        Assert.Equal(corpo.GetProperty("ocorridoEm").GetDateTimeOffset(), primeiro.GetProperty("ocorridoEm").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("0", "valor")]
    [InlineData("-5.00", "valor")]
    [InlineData("10.001", "valor")]
    [InlineData("10.00", "descricao")]
    public async Task Valor_ou_descricao_invalidos_retornam_400_em_Problem_Details_com_o_campo_e_nada_e_gravado(string valor, string campo)
    {
        var descricao = campo == "descricao" ? new string('x', 141) : "Lançamento";
        var chave = NovaChave();

        using var resposta = await _operador.RegistrarAsync(
            "CC-0001", new { tipo = "DEBITO", valor = decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture), descricao }, chave);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        var corpo = await resposta.LerJsonAsync();
        Assert.True(corpo.GetProperty("errors").TryGetProperty(campo, out _), $"Esperado erro no campo {campo}: {corpo}");
        Assert.Equal(0, await ContarPorChaveAsync(chave));
    }

    [Fact]
    public async Task Tipo_desconhecido_ou_corpo_ausente_retornam_400()
    {
        using var tipoInvalido = await _operador.RegistrarAsync("CC-0001", new { tipo = "PIX", valor = 1m, descricao = "x" }, NovaChave());
        Assert.Equal(HttpStatusCode.BadRequest, tipoInvalido.StatusCode);
        Assert.True((await tipoInvalido.LerJsonAsync()).GetProperty("errors").TryGetProperty("tipo", out _));

        using var semCorpo = new HttpRequestMessage(HttpMethod.Post, "/api/v1/contas/CC-0001/lancamentos");
        semCorpo.Headers.Add("Idempotency-Key", NovaChave());
        using var respostaSemCorpo = await _operador.SendAsync(semCorpo);
        Assert.Equal(HttpStatusCode.BadRequest, respostaSemCorpo.StatusCode);
    }

    [Fact]
    public async Task Conta_inexistente_retorna_404_e_conta_inativa_retorna_422_sem_gravar()
    {
        var corpo = new { tipo = "CREDITO", valor = 10m, descricao = "x" };
        var chaveInexistente = NovaChave();
        var chaveInativa = NovaChave();
        var inativa = await _fabrica.CriarContaAsync(SituacaoConta.Inativa);

        using var inexistente = await _operador.RegistrarAsync("CC-9999", corpo, chaveInexistente);
        using var naInativa = await _operador.RegistrarAsync(inativa, corpo, chaveInativa);

        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, naInativa.StatusCode);
        Assert.Equal(0, await ContarPorChaveAsync(chaveInexistente));
        Assert.Equal(0, await ContarPorChaveAsync(chaveInativa));
    }

    [Fact]
    public async Task Usuario_so_com_papel_consulta_recebe_403_ao_registrar()
    {
        using var consulta = _fabrica.ClienteComPapel("consulta");

        using var resposta = await consulta.RegistrarAsync("CC-0001", new { tipo = "CREDITO", valor = 10m, descricao = "x" }, NovaChave());

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }
}
