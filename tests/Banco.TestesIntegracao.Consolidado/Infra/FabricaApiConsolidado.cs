using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Banco.Aplicacao.Compartilhado.Contratos;
using Banco.Aplicacao.Consolidado;
using Banco.Testes.Comum;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Banco.TestesIntegracao.Consolidado.Infra;

/// <summary>
/// Sobe o <c>Banco.Api.Consolidado</c> real (migração na subida) contra os containers de <see cref="InfraConsolidado"/>,
/// validando JWTs assinados pela chave de teste com as policies de produção. <paramref name="configuracao"/> sobrescreve
/// chaves (ex.: Redis apontando para uma porta sem servidor) e <paramref name="relogio"/> fixa o "agora" da Api.
/// </summary>
internal sealed class FabricaApiConsolidado(
    InfraConsolidado infra,
    IDictionary<string, string?>? configuracao = null,
    TimeProvider? relogio = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        foreach (var (chave, valor) in infra.Configuracao())
        {
            builder.UseSetting(chave, valor);
        }

        foreach (var (chave, valor) in configuracao ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(chave, valor);
        }

        builder.ConfigureTestServices(servicos =>
        {
            servicos.ValidarTokensComChaveDeTeste();
            if (relogio is not null)
            {
                servicos.RemoveAll<TimeProvider>();
                servicos.AddSingleton(relogio);
            }
        });
    }

    /// <summary>Cliente HTTP com um token de teste do papel informado (a validade do JWT segue o relógio real).</summary>
    public HttpClient ClienteComPapel(string papel)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokensTeste.Emitir(sub: papel, papeis: [papel]));
        return cliente;
    }

    /// <summary>Projeta lançamentos pelo mesmo caso de uso do Worker (inbox, resumo, movimento, marca e versão no Redis).</summary>
    public async Task ProjetarAsync(params LancamentoRegistradoV1[] eventos)
    {
        foreach (var evento in eventos)
        {
            await using var escopo = Services.CreateAsyncScope();
            await escopo.ServiceProvider.GetRequiredService<ProjetarLancamento>().ExecutarAsync(evento, CancellationToken.None);
        }
    }

    public static LancamentoRegistradoV1 Credito(string conta, decimal valor, string ocorridoEm) =>
        new(Guid.CreateVersion7(), conta, TipoLancamentoV1.Credito, valor, DateTimeOffset.Parse(ocorridoEm, CultureInfo.InvariantCulture), EstornoDe: null);

    public static LancamentoRegistradoV1 Debito(string conta, decimal valor, string ocorridoEm) =>
        new(Guid.CreateVersion7(), conta, TipoLancamentoV1.Debito, valor, DateTimeOffset.Parse(ocorridoEm, CultureInfo.InvariantCulture), EstornoDe: null);

    public static async Task<JsonElement> LerJsonAsync(HttpResponseMessage resposta)
    {
        ArgumentNullException.ThrowIfNull(resposta);
        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return documento.RootElement.Clone();
    }
}

/// <summary>Relógio fixo, para o "agora" da Api ser determinístico.</summary>
internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
