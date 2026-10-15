using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Banco.Infraestrutura.Compartilhado;
using Banco.Infraestrutura.Compartilhado.Autenticacao;
using Banco.TestesIntegracao.Identidade.Infra;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Banco.TestesIntegracao.Identidade;

// Spec access-control, requisito "Publicação da chave de validação".
[Collection(ColecaoIdentidade.Nome)]
public sealed class ChavePublicaTestes : IDisposable
{
    private readonly FabricaApiIdentidade _fabrica;
    private readonly HttpClient _cliente;

    public ChavePublicaTestes(PostgresIdentidade postgres)
    {
        _fabrica = new FabricaApiIdentidade(postgres);
        _cliente = _fabrica.CreateClient();
    }

    public void Dispose()
    {
        _cliente.Dispose();
        _fabrica.Dispose();
    }

    [Fact]
    public async Task Descoberta_aponta_para_o_JWKS_que_publica_a_chave_publica_com_o_kid_do_token()
    {
        var token = await ObterTokenAsync();

        using var descoberta = JsonDocument.Parse(await _cliente.GetStringAsync("/.well-known/openid-configuration"));
        Assert.Equal("banco-identidade", descoberta.RootElement.GetProperty("issuer").GetString());
        var jwksUri = new Uri(descoberta.RootElement.GetProperty("jwks_uri").GetString()!);
        Assert.Equal("/.well-known/jwks.json", jwksUri.AbsolutePath);

        using var jwks = JsonDocument.Parse(await _cliente.GetStringAsync(jwksUri));
        var chave = Assert.Single(jwks.RootElement.GetProperty("keys").EnumerateArray());
        Assert.Equal(new JsonWebToken(token).Kid, chave.GetProperty("kid").GetString());
        Assert.Equal("RSA", chave.GetProperty("kty").GetString());
        Assert.Equal("RS256", chave.GetProperty("alg").GetString());
        Assert.Equal("sig", chave.GetProperty("use").GetString());
        Assert.False(chave.TryGetProperty("d", out _), "o JWKS não pode expor a parte privada da chave");
    }

    [Fact]
    public async Task Token_emitido_e_aceito_por_uma_api_cujo_JwtBearer_conhece_so_o_MetadataAddress()
    {
        var token = await ObterTokenAsync();

        // Outra Api com os padrões de produção (AdicionarPadroesBanco), apontando só para a descoberta da
        // Identidade; o TestServer da Identidade atende o backchannel do JwtBearer.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Autenticacao:EnderecoMetadados"] = new Uri(_fabrica.Server.BaseAddress, "/.well-known/openid-configuration").ToString(),
        });
        builder.AdicionarPadroesBanco();
        builder.Services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            opcoes => opcoes.BackchannelHttpHandler = _fabrica.Server.CreateHandler());
        await using var api = builder.Build();
        api.UsarPadroesBanco();
        api.MapGet("/api/v1/contas", (HttpContext contexto) => contexto.User.FindFirst("sub")!.Value)
            .RequireAuthorization(PoliticasAutorizacao.PodeOperar);
        await api.StartAsync();
        using var clienteApi = api.GetTestClient();

        using var semToken = await clienteApi.GetAsync("/api/v1/contas");
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, "/api/v1/contas");
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var comToken = await clienteApi.SendAsync(requisicao);

        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, comToken.StatusCode);
        Assert.Equal(new JsonWebToken(token).Subject, await comToken.Content.ReadAsStringAsync());
    }

    private async Task<string> ObterTokenAsync()
    {
        using var resposta = await _cliente.PostAsJsonAsync("/api/v1/autenticacao/login", new { login = "operador", senha = "Operador@123" });
        resposta.EnsureSuccessStatusCode();
        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return corpo.RootElement.GetProperty("tokenAcesso").GetString()!;
    }
}
