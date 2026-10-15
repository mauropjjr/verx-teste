using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Banco.TestesIntegracao.Identidade.Infra;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Banco.TestesIntegracao.Identidade;

// Spec access-control, requisito "Login com usuário e senha" (usuários de demonstração do seed).
[Collection(ColecaoIdentidade.Nome)]
public sealed class LoginTestes : IDisposable
{
    private const string Rota = "/api/v1/autenticacao/login";

    private readonly FabricaApiIdentidade _fabrica;
    private readonly HttpClient _cliente;

    public LoginTestes(PostgresIdentidade postgres)
    {
        _fabrica = new FabricaApiIdentidade(postgres);
        _cliente = _fabrica.CreateClient();
    }

    public void Dispose()
    {
        _cliente.Dispose();
        _fabrica.Dispose();
    }

    private Task<HttpResponseMessage> LogarAsync(string login, string senha) =>
        _cliente.PostAsJsonAsync(Rota, new { login, senha });

    [Fact]
    public async Task Login_valido_retorna_200_com_tokenAcesso_tipo_Bearer_e_expiraEm_e_o_papel_do_usuario()
    {
        using var resposta = await LogarAsync("operador", "Operador@123");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.Equal("Bearer", corpo.RootElement.GetProperty("tipo").GetString());
        var expiraEm = corpo.RootElement.GetProperty("expiraEm").GetDateTimeOffset();
        Assert.Equal(_fabrica.Relogio.GetUtcNow().AddMinutes(30), expiraEm, TimeSpan.FromSeconds(1));

        var token = new JsonWebToken(corpo.RootElement.GetProperty("tokenAcesso").GetString());
        Assert.Contains(token.Claims, c => c.Type == "role" && c.Value == "operador");
        Assert.Equal("Operador Demo", token.GetClaim("name").Value);

        // Pronto para tráfego: o Postgres é dependência verificada pelo /health/ready.
        using var pronto = await _cliente.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, pronto.StatusCode);
    }

    [Fact]
    public async Task Senha_errada_e_usuario_inexistente_retornam_401_com_a_mesma_mensagem_e_a_senha_nunca_vai_para_o_log()
    {
        using var senhaErrada = await LogarAsync("admin", "SenhaErradaSecreta#1");
        using var inexistente = await LogarAsync("fulano", "OutraSenhaSecreta#2");
        using var valido = await LogarAsync("admin", "Admin@123");

        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valido.StatusCode);
        using var corpoSenhaErrada = JsonDocument.Parse(await senhaErrada.Content.ReadAsStringAsync());
        using var corpoInexistente = JsonDocument.Parse(await inexistente.Content.ReadAsStringAsync());
        var mensagem = corpoSenhaErrada.RootElement.GetProperty("detail").GetString();
        Assert.False(string.IsNullOrWhiteSpace(mensagem));
        Assert.Equal(mensagem, corpoInexistente.RootElement.GetProperty("detail").GetString());
        Assert.Equal(
            corpoSenhaErrada.RootElement.GetProperty("title").GetString(),
            corpoInexistente.RootElement.GetProperty("title").GetString());

        var logs = _fabrica.Logs.ComoJson();
        Assert.NotEmpty(_fabrica.Logs.Eventos);
        Assert.DoesNotContain("SenhaErradaSecreta#1", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("OutraSenhaSecreta#2", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Admin@123", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sexta_tentativa_apos_5_falhas_retorna_401_mesmo_com_senha_certa_ate_o_fim_do_bloqueio()
    {
        for (var i = 0; i < 5; i++)
        {
            using var falha = await LogarAsync("consulta", "errada");
            Assert.Equal(HttpStatusCode.Unauthorized, falha.StatusCode);
        }

        using (var sexta = await LogarAsync("consulta", "Consulta@123"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, sexta.StatusCode);
        }

        _fabrica.Relogio.Avancar(TimeSpan.FromMinutes(14));
        using (var aindaBloqueado = await LogarAsync("consulta", "Consulta@123"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, aindaBloqueado.StatusCode);
        }

        _fabrica.Relogio.Avancar(TimeSpan.FromMinutes(1));
        using var aposBloqueio = await LogarAsync("consulta", "Consulta@123");
        Assert.Equal(HttpStatusCode.OK, aposBloqueio.StatusCode);
    }

    [Fact]
    public async Task Entrada_invalida_retorna_400()
    {
        using var semSenha = await _cliente.PostAsJsonAsync(Rota, new { login = "operador" });
        using var loginLongo = await LogarAsync(new string('a', 101), "x");

        Assert.Equal(HttpStatusCode.BadRequest, semSenha.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, loginLongo.StatusCode);
    }
}
