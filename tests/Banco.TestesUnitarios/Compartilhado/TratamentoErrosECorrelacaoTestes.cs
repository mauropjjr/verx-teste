using System.Net;
using System.Text.Json;
using Banco.Testes.Comum;
using Banco.TestesUnitarios.Compartilhado.Infra;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Banco.TestesUnitarios.Compartilhado;

public class TratamentoErrosECorrelacaoTestes
{
    private const string CabecalhoCorrelacao = "X-Correlation-Id";

    private static readonly Action<ILogger, Exception?> _logProcessando =
        LoggerMessage.Define(LogLevel.Information, new EventId(1), "Processando requisição");

    private static Task<HostTeste> IniciarComEndpointQueFalhaAsync(SinkMemoria sink) =>
        HostTeste.IniciarAsync(
            configurarServicos: b => b.Services.AddSingleton<ILogEventSink>(sink),
            mapearEndpoints: app =>
            {
                app.MapGet("/falha", string () => throw new InvalidOperationException("detalhe-interno-secreto"))
                    .AllowAnonymous();
                app.MapGet("/ok", (ILogger<TratamentoErrosECorrelacaoTestes> logger) =>
                {
                    _logProcessando(logger, null);
                    return "ok";
                }).AllowAnonymous();
            });

    [Fact]
    public async Task Excecao_nao_tratada_retorna_500_problem_details_com_traceId_sem_detalhes_e_loga_traceId_e_correlacao()
    {
        var sink = new SinkMemoria();
        await using var host = await IniciarComEndpointQueFalhaAsync(sink);
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, "/falha");
        requisicao.Headers.Add(CabecalhoCorrelacao, "C1");

        var resposta = await host.Cliente.SendAsync(requisicao);
        var corpo = await resposta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("detalhe-interno-secreto", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", corpo, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(corpo);
        var traceId = json.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());

        var erroLogado = Assert.Single(sink.Eventos, e => e.Level == LogEventLevel.Error && e.Exception is not null);
        Assert.Equal("\"C1\"", erroLogado.Properties["CorrelationId"].ToString());
        Assert.Equal($"\"{traceId}\"", erroLogado.Properties["TraceId"].ToString());
    }

    [Fact]
    public async Task X_Correlation_Id_valido_e_devolvido_e_ausente_ou_invalido_e_gerado()
    {
        var sink = new SinkMemoria();
        await using var host = await IniciarComEndpointQueFalhaAsync(sink);

        // Aceito: o mesmo valor volta na resposta e aparece nos logs escritos durante a requisição.
        using (var comCabecalho = new HttpRequestMessage(HttpMethod.Get, "/ok"))
        {
            comCabecalho.Headers.Add(CabecalhoCorrelacao, "pedido-123");
            var resposta = await host.Cliente.SendAsync(comCabecalho);
            Assert.Equal("pedido-123", Assert.Single(resposta.Headers.GetValues(CabecalhoCorrelacao)));
            Assert.Contains(sink.Eventos, e =>
                e.Properties.TryGetValue("CorrelationId", out var valor) && valor.ToString() == "\"pedido-123\"");
        }

        // Ausente: um id novo é gerado e devolvido.
        var semCabecalho = await host.Cliente.GetAsync(new Uri("/ok", UriKind.Relative));
        var gerado = Assert.Single(semCabecalho.Headers.GetValues(CabecalhoCorrelacao));
        Assert.False(string.IsNullOrWhiteSpace(gerado));

        // Inválido (longo demais ou com caracteres de injeção em log): descartado e substituído.
        foreach (var invalido in new[] { new string('a', 200), "abc\" injetado" })
        {
            using var requisicao = new HttpRequestMessage(HttpMethod.Get, "/ok");
            requisicao.Headers.TryAddWithoutValidation(CabecalhoCorrelacao, invalido);
            var resposta = await host.Cliente.SendAsync(requisicao);
            var devolvido = Assert.Single(resposta.Headers.GetValues(CabecalhoCorrelacao));
            Assert.NotEqual(invalido, devolvido);
            Assert.False(string.IsNullOrWhiteSpace(devolvido));
        }
    }
}
