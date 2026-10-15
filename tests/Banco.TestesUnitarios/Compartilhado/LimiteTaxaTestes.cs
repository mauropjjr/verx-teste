using System.Net;
using System.Net.Http.Headers;
using Banco.Infraestrutura.Compartilhado.LimiteTaxa;
using Banco.Testes.Comum;
using Banco.TestesUnitarios.Compartilhado.Infra;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Banco.TestesUnitarios.Compartilhado;

public class LimiteTaxaTestes
{
    private const int Limite = 3;

    // Rajada bem acima do limite: mesmo que uma reposição do bucket (1 s) ocorra no meio, sobra excedente.
    private const int Rajada = (Limite * 3) + 1;

    private static Task<HostTeste> IniciarAsync() =>
        HostTeste.IniciarAsync(
            configurarServicos: b => b.Services.ValidarTokensComChaveDeTeste(),
            mapearEndpoints: app =>
            {
                app.MapGet("/api/v1/contas", () => "ok");
                app.MapGet("/publico", () => "ok").AllowAnonymous();
                app.MapPost("/login", () => "ok").AllowAnonymous().RequireRateLimiting(PoliticasLimiteTaxa.LoginPorIp);
            },
            configuracao: new Dictionary<string, string?>
            {
                ["LimiteTaxa:RequisicoesPorSegundo"] = Limite.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["LimiteTaxa:LoginPorIpPorMinuto"] = "2",
            });

    private static async Task<List<HttpResponseMessage>> EnviarRajadaAsync(HostTeste host, HttpMethod metodo, string rota, string? token, int quantidade)
    {
        var respostas = new List<HttpResponseMessage>();
        for (var i = 0; i < quantidade; i++)
        {
            using var requisicao = new HttpRequestMessage(metodo, rota);
            if (token is not null)
            {
                requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            respostas.Add(await host.Cliente.SendAsync(requisicao));
        }

        return respostas;
    }

    [Fact]
    public async Task Usuario_que_excede_o_limite_por_sub_recebe_429_com_Retry_After_sem_afetar_outro_usuario()
    {
        await using var host = await IniciarAsync();
        var tokenAna = TokensTeste.Emitir(sub: "ana", papeis: ["consulta"]);
        var tokenBruno = TokensTeste.Emitir(sub: "bruno", papeis: ["consulta"]);

        var respostasAna = await EnviarRajadaAsync(host, HttpMethod.Get, "/api/v1/contas", tokenAna, Rajada);

        Assert.All(respostasAna.Take(Limite), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Contains(respostasAna, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        var rejeitada = respostasAna.First(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.True(rejeitada.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        // Partição por sub: outro usuário tem o próprio bucket.
        var respostaBruno = Assert.Single(await EnviarRajadaAsync(host, HttpMethod.Get, "/api/v1/contas", tokenBruno, 1));
        Assert.Equal(HttpStatusCode.OK, respostaBruno.StatusCode);

        // Anônimo: limitado por IP.
        var anonimas = await EnviarRajadaAsync(host, HttpMethod.Get, "/publico", null, Rajada);
        Assert.Contains(anonimas, r => r.StatusCode == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_tem_politica_propria_por_IP_e_responde_429_ao_exceder()
    {
        await using var host = await IniciarAsync();

        var respostas = await EnviarRajadaAsync(host, HttpMethod.Post, "/login", null, 3);

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            respostas.Select(r => r.StatusCode));
        Assert.NotNull(respostas[2].Headers.RetryAfter?.Delta);
    }
}
