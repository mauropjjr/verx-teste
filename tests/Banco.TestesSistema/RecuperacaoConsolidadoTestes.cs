using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Banco.TestesSistema.Infra;

namespace Banco.TestesSistema;

// Spec balance-consolidation, cenário "Recuperação após indisponibilidade do consolidado", e ledger-entries
// (lançamentos continuam disponíveis com o consolidado fora do ar). Roda contra o compose em execução.
public sealed class RecuperacaoConsolidadoTestes : IDisposable
{
    private const int QuantidadeLancamentos = 100;
    private static readonly string[] _servicosConsolidado = ["consolidado-worker", "consolidado-api"];
    private static readonly TimeSpan _tempoMaximoConvergencia = TimeSpan.FromMinutes(3);

    private readonly HttpClient _identidade = new() { BaseAddress = AmbienteSistema.Identidade };
    private readonly HttpClient _lancamentos = new() { BaseAddress = AmbienteSistema.Lancamentos };
    private readonly HttpClient _consolidado = new() { BaseAddress = AmbienteSistema.Consolidado };

    public void Dispose()
    {
        _identidade.Dispose();
        _lancamentos.Dispose();
        _consolidado.Dispose();
    }

    [FatoSistema]
    public async Task Com_worker_e_api_do_consolidado_parados_100_lancamentos_sao_aceitos_e_o_saldo_converge_quando_voltam()
    {
        var conta = AmbienteSistema.Conta;
        var token = await LogarAsync("operador", AmbienteSistema.SenhaOperador);
        foreach (var cliente in new[] { _lancamentos, _consolidado })
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // Saldo anterior pela fonte de verdade (o razão do serviço de lançamentos), não pelo consolidado — que
        // pode ainda estar processando o seed da primeira subida.
        var saldoAnterior = await SomarRazaoAsync(conta);

        await ComposeDocker.PararAsync(_servicosConsolidado);
        decimal somaLiquida;
        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => _consolidado.GetAsync($"/api/v1/contas/{conta}/saldo"));
            // Balde do limite de taxa (100 req/s por usuário) cheio de novo depois das páginas do extrato.
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            somaLiquida = await RegistrarLancamentosAsync(conta);
        }
        finally
        {
            await ComposeDocker.IniciarAsync(_servicosConsolidado);
        }

        var esperado = saldoAnterior + somaLiquida;
        var ultimo = await AguardarSaldoAsync(conta, esperado);
        Assert.Equal(esperado, ultimo);
    }

    private async Task<string> LogarAsync(string login, string senha)
    {
        using var resposta = await _identidade.PostAsJsonAsync("/api/v1/autenticacao/login", new { login, senha });
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await LerJsonAsync(resposta)).GetProperty("tokenAcesso").GetString()!;
    }

    private async Task<decimal> SomarRazaoAsync(string conta)
    {
        var soma = 0m;
        string? cursor = null;
        do
        {
            var rota = $"/api/v1/contas/{conta}/lancamentos?limite=100" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            using var resposta = await _lancamentos.GetAsync(rota);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var pagina = await LerJsonAsync(resposta);
            soma += pagina.GetProperty("itens").EnumerateArray().Sum(item => Liquido(item.GetProperty("tipo").GetString()!, item.GetProperty("valor").GetDecimal()));
            cursor = pagina.GetProperty("proximoCursor").GetString();
        }
        while (cursor is not null);

        return soma;
    }

    private async Task<decimal> RegistrarLancamentosAsync(string conta)
    {
        var somaLiquida = 0m;
        for (var i = 1; i <= QuantidadeLancamentos; i++)
        {
            var tipo = i % 3 == 0 ? "DEBITO" : "CREDITO";
            var valor = i + 0.37m;
            using var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/contas/{conta}/lancamentos")
            {
                Content = JsonContent.Create(new { tipo, valor, descricao = string.Create(CultureInfo.InvariantCulture, $"Teste de sistema {i}") }),
            };
            requisicao.Headers.Add("Idempotency-Key", $"sistema-{Guid.NewGuid():N}");

            using var resposta = await _lancamentos.SendAsync(requisicao);
            Assert.True(resposta.StatusCode == HttpStatusCode.Created, $"Lançamento {i}: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}");
            somaLiquida += Liquido(tipo, valor);
        }

        return somaLiquida;
    }

    /// <summary>Consulta o saldo até bater com o esperado ou estourar o tempo; devolve a última leitura.</summary>
    private async Task<decimal?> AguardarSaldoAsync(string conta, decimal esperado)
    {
        decimal? ultimo = null;
        var cronometro = Stopwatch.StartNew();
        while (cronometro.Elapsed < _tempoMaximoConvergencia)
        {
            try
            {
                using var resposta = await _consolidado.GetAsync($"/api/v1/contas/{conta}/saldo");
                if (resposta.StatusCode == HttpStatusCode.OK)
                {
                    ultimo = (await LerJsonAsync(resposta)).GetProperty("saldo").GetDecimal();
                    if (ultimo == esperado)
                    {
                        return ultimo;
                    }
                }
            }
            catch (HttpRequestException)
            {
                // A Api do consolidado ainda está subindo.
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        return ultimo;
    }

    private static decimal Liquido(string tipo, decimal valor) => tipo == "CREDITO" ? valor : -valor;

    private static async Task<JsonElement> LerJsonAsync(HttpResponseMessage resposta)
    {
        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return documento.RootElement.Clone();
    }
}
