using System.Net.Http.Headers;
using System.Net.Http.Json;
using Banco.Testes.Comum;
using Banco.TestesUnitarios.Compartilhado.Infra;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;

namespace Banco.TestesUnitarios.Compartilhado;

public class FiltroDadosSensiveisTestes
{
    private sealed record RequisicaoLogin(string Login, string Senha);

    private static readonly Action<ILogger, RequisicaoLogin, Exception?> _logLogin =
        LoggerMessage.Define<RequisicaoLogin>(LogLevel.Information, new EventId(1), "Login recebido {@Requisicao}");

    private static readonly Action<ILogger, Dictionary<string, string>, Exception?> _logCabecalhos =
        LoggerMessage.Define<Dictionary<string, string>>(LogLevel.Information, new EventId(2), "Cabeçalhos {@Cabecalhos}");

    private static readonly Action<ILogger, string, Exception?> _logAuthorization =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(3), "Recebido {Authorization}");

    [Fact]
    public async Task Logs_da_requisicao_mascaram_Authorization_e_senha()
    {
        var sink = new SinkMemoria();
        await using var host = await HostTeste.IniciarAsync(
            configurarServicos: b => b.Services.AddSingleton<ILogEventSink>(sink).ValidarTokensComChaveDeTeste(),
            mapearEndpoints: app => app.MapPost("/login", (RequisicaoLogin corpo, HttpContext contexto, ILogger<FiltroDadosSensiveisTestes> logger) =>
            {
                // Um descuido típico: logar o corpo e os cabeçalhos inteiros.
                _logLogin(logger, corpo, null);
                _logCabecalhos(logger, contexto.Request.Headers.ToDictionary(c => c.Key, c => c.Value.ToString()), null);
                _logAuthorization(logger, contexto.Request.Headers.Authorization.ToString(), null);
                return "ok";
            }).AllowAnonymous());
        var token = TokensTeste.Emitir(papeis: ["operador"]);
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "/login")
        {
            Content = JsonContent.Create(new { login = "operador", senha = "segredo-123" }),
        };
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resposta = await host.Cliente.SendAsync(requisicao);
        var logs = sink.ComoJson();

        resposta.EnsureSuccessStatusCode();
        Assert.Contains("operador", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("segredo-123", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", logs, StringComparison.Ordinal);
        Assert.Contains("***", logs, StringComparison.Ordinal);
    }
}
