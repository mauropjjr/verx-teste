using System.Net;
using System.Text.Json;
using Banco.Dominio.Lancamentos;
using Banco.TestesIntegracao.Lancamentos.Infra;

namespace Banco.TestesIntegracao.Lancamentos;

// Spec ledger-entries, requisito "Consultar extrato de lançamentos": ordem decrescente, filtro por datas,
// paginação por cursor e página de no máximo 100.
[Collection(ColecaoLancamentos.Nome)]
public sealed class ExtratoTestes : IDisposable
{
    private static readonly DateTimeOffset _inicio = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly FabricaApiLancamentos _fabrica;
    private readonly HttpClient _consulta;

    public ExtratoTestes(PostgresLancamentos postgres)
    {
        _fabrica = new FabricaApiLancamentos(postgres);
        _consulta = _fabrica.ClienteComPapel("consulta");
    }

    public void Dispose()
    {
        _consulta.Dispose();
        _fabrica.Dispose();
    }

    /// <summary>
    /// 250 lançamentos de 5 em 5 por hora a partir de 01/03 00h (empates de instante desempatados pelo id):
    /// 120 em 01/03, 120 em 02/03 e 10 em 03/03.
    /// </summary>
    private async Task<(string ContaId, IReadOnlyList<Lancamento> Lancamentos)> ContaCom250LancamentosAsync()
    {
        var contaId = await _fabrica.CriarContaAsync();
        var lancamentos = Enumerable.Range(0, 250)
            .Select(i => Lancamento.Registrar(
                contaId,
                i % 2 == 0 ? TipoLancamento.Credito : TipoLancamento.Debito,
                i + 1,
                $"Lançamento {i}",
                _inicio.AddHours(i / 5),
                $"semente-{i}",
                new string('0', 64)))
            .ToList();
        await _fabrica.ExecutarNoBancoAsync(async c =>
        {
            c.Lancamentos.AddRange(lancamentos);
            await c.SaveChangesAsync();
        });
        return (contaId, lancamentos);
    }

    private async Task<List<JsonElement>> PercorrerAsync(string rotaBase, List<int> tamanhosPaginas)
    {
        var itens = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var rota = cursor is null ? rotaBase : $"{rotaBase}&cursor={Uri.EscapeDataString(cursor)}";
            using var resposta = await _consulta.GetAsync(rota);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var corpo = await resposta.LerJsonAsync();
            var pagina = corpo.GetProperty("itens").EnumerateArray().ToList();
            tamanhosPaginas.Add(pagina.Count);
            itens.AddRange(pagina);
            var proximo = corpo.GetProperty("proximoCursor");
            cursor = proximo.ValueKind == JsonValueKind.Null ? null : proximo.GetString();
        }
        while (cursor is not null && tamanhosPaginas.Count < 10);

        return itens;
    }

    [Fact]
    public async Task Extrato_de_250_lancamentos_pagina_por_cursor_em_ordem_decrescente_sem_repetir_nem_omitir()
    {
        var (contaId, lancamentos) = await ContaCom250LancamentosAsync();
        var tamanhos = new List<int>();

        var itens = await PercorrerAsync($"/api/v1/contas/{contaId}/lancamentos?limite=100", tamanhos);

        Assert.Equal([100, 100, 50], tamanhos);
        var ids = itens.Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(250, ids.Distinct().Count());
        Assert.Equal(lancamentos.Select(l => l.Id).Order(), ids.Order());
        var instantes = itens.Select(i => i.GetProperty("ocorridoEm").GetDateTimeOffset()).ToList();
        Assert.Equal(instantes.OrderDescending(), instantes);
        Assert.Equal(_inicio.AddHours(49), instantes[0]);
        Assert.Equal(contaId, itens[0].GetProperty("contaId").GetString());
        Assert.Matches("^(CREDITO|DEBITO)$", itens[0].GetProperty("tipo").GetString());
    }

    [Fact]
    public async Task Extrato_filtra_por_intervalo_de_datas_inclusivo_e_pagina_dentro_do_filtro()
    {
        var (contaId, _) = await ContaCom250LancamentosAsync();
        var tamanhos = new List<int>();

        var dia2 = await PercorrerAsync($"/api/v1/contas/{contaId}/lancamentos?de=2026-03-02&ate=2026-03-02&limite=100", tamanhos);
        var aPartirDia3 = await PercorrerAsync($"/api/v1/contas/{contaId}/lancamentos?de=2026-03-03", []);
        var ateDia1 = await PercorrerAsync($"/api/v1/contas/{contaId}/lancamentos?ate=2026-03-01&limite=100", []);

        Assert.Equal([100, 20], tamanhos);
        Assert.Equal(120, dia2.Select(i => i.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.All(dia2, i => Assert.Equal(new DateOnly(2026, 3, 2), DateOnly.FromDateTime(i.GetProperty("ocorridoEm").GetDateTimeOffset().UtcDateTime)));
        Assert.Equal(10, aPartirDia3.Count);
        Assert.Equal(120, ateDia1.Count);
    }

    [Fact]
    public async Task Extrato_recusa_limite_acima_de_100_e_parametros_invalidos_com_400_e_conta_inexistente_com_404()
    {
        string[] invalidos =
        [
            "limite=101",
            "limite=0",
            "cursor=nao-e-um-cursor",
            "de=2026-03-02&ate=2026-03-01",
        ];
        foreach (var consulta in invalidos)
        {
            using var resposta = await _consulta.GetAsync($"/api/v1/contas/CC-0001/lancamentos?{consulta}");
            Assert.True(resposta.StatusCode == HttpStatusCode.BadRequest, $"{consulta} deveria dar 400, deu {resposta.StatusCode}");
            Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        }

        using var limite = await _consulta.GetAsync("/api/v1/contas/CC-0001/lancamentos?limite=101");
        Assert.True((await limite.LerJsonAsync()).GetProperty("errors").TryGetProperty("limite", out _));

        using var inexistente = await _consulta.GetAsync("/api/v1/contas/CC-9999/lancamentos");
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }
}
