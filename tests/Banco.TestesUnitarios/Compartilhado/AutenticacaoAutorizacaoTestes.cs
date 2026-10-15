using System.Net;
using System.Net.Http.Headers;
using Banco.Infraestrutura.Compartilhado.Autenticacao;
using Banco.Testes.Comum;
using Banco.TestesUnitarios.Compartilhado.Infra;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Banco.TestesUnitarios.Compartilhado;

public class AutenticacaoAutorizacaoTestes
{
    private static Task<HostTeste> IniciarAsync() =>
        HostTeste.IniciarAsync(
            configurarServicos: b => b.Services.ValidarTokensComChaveDeTeste(),
            mapearEndpoints: app =>
            {
                // Sem metadata de autorização explícita: deve exigir autenticação por padrão.
                app.MapGet("/api/v1/contas", () => "ok");
                app.MapGet("/consultar", () => "ok").RequireAuthorization(PoliticasAutorizacao.PodeConsultar);
                app.MapPost("/operar", () => "ok").RequireAuthorization(PoliticasAutorizacao.PodeOperar);
                app.MapPost("/administrar", () => "ok").RequireAuthorization(PoliticasAutorizacao.PodeAdministrar);
            });

    private static async Task<HttpStatusCode> EnviarAsync(HostTeste host, HttpMethod metodo, string rota, string? token)
    {
        using var requisicao = new HttpRequestMessage(metodo, rota);
        if (token is not null)
        {
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var resposta = await host.Cliente.SendAsync(requisicao);
        return resposta.StatusCode;
    }

    [Fact]
    public async Task Requisicao_sem_token_retorna_401()
    {
        await using var host = await IniciarAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await EnviarAsync(host, HttpMethod.Get, "/api/v1/contas", null));
        Assert.Equal(HttpStatusCode.Unauthorized, await EnviarAsync(host, HttpMethod.Get, "/consultar", null));
    }

    [Fact]
    public async Task Token_de_outro_emissor_outra_audiencia_expirado_ou_com_assinatura_invalida_retorna_401()
    {
        await using var host = await IniciarAsync();
        var papeis = new[] { "admin" };
        var tokensInvalidos = new Dictionary<string, string>
        {
            ["outro emissor"] = TokensTeste.Emitir(papeis: papeis, emissor: "emissor-estranho"),
            ["outra audiência"] = TokensTeste.Emitir(papeis: papeis, audiencia: "outra-api"),
            ["expirado"] = TokensTeste.Emitir(papeis: papeis, expiraEmUtc: DateTime.UtcNow.AddMinutes(-10)),
            ["assinatura inválida"] = TokensTeste.Emitir(papeis: papeis, chaveAssinatura: TokensTeste.NovaChaveNaoConfiavel()),
        };

        // Controle: o mesmo token, válido, passa.
        Assert.Equal(HttpStatusCode.OK, await EnviarAsync(host, HttpMethod.Get, "/consultar", TokensTeste.Emitir(papeis: papeis)));
        foreach (var (caso, token) in tokensInvalidos)
        {
            var status = await EnviarAsync(host, HttpMethod.Get, "/consultar", token);
            Assert.True(status == HttpStatusCode.Unauthorized, $"{caso}: esperado 401, obtido {(int)status}");
        }
    }

    // Matriz de papéis × policies do design D10.
    [Theory]
    [InlineData("consulta", "/consultar", HttpStatusCode.OK)]
    [InlineData("consulta", "/operar", HttpStatusCode.Forbidden)]
    [InlineData("consulta", "/administrar", HttpStatusCode.Forbidden)]
    [InlineData("operador", "/consultar", HttpStatusCode.OK)]
    [InlineData("operador", "/operar", HttpStatusCode.OK)]
    [InlineData("operador", "/administrar", HttpStatusCode.Forbidden)]
    [InlineData("admin", "/consultar", HttpStatusCode.OK)]
    [InlineData("admin", "/operar", HttpStatusCode.OK)]
    [InlineData("admin", "/administrar", HttpStatusCode.OK)]
    public async Task Policies_aplicam_os_papeis_consulta_operador_e_admin(string papel, string rota, HttpStatusCode esperado)
    {
        await using var host = await IniciarAsync();
        var metodo = rota == "/consultar" ? HttpMethod.Get : HttpMethod.Post;

        var status = await EnviarAsync(host, metodo, rota, TokensTeste.Emitir(papeis: [papel]));

        Assert.Equal(esperado, status);
    }
}
