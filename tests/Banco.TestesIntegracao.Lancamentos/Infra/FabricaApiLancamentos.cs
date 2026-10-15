using System.Net.Http.Headers;
using Banco.Dominio.Lancamentos;
using Banco.Infraestrutura.Lancamentos.Persistencia;
using Banco.Testes.Comum;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.TestesIntegracao.Lancamentos.Infra;

/// <summary>
/// Sobe o <c>Banco.Api.Lancamentos</c> real (migração + seed das contas demo no startup) contra o Postgres do
/// Testcontainers, validando JWTs assinados pela chave de teste com as policies de produção. Sem
/// <paramref name="ajustar"/>, o publicador do outbox aponta para uma porta sem broker (as mensagens ficam
/// pendentes e nenhum RabbitMQ local é tocado); os testes de publicação passam o broker do Testcontainers.
/// </summary>
internal sealed class FabricaApiLancamentos(PostgresLancamentos postgres, Action<IWebHostBuilder>? ajustar = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Lancamentos", postgres.StringConexao);
        builder.UseSetting("RabbitMq:Host", "127.0.0.1");
        builder.UseSetting("RabbitMq:Porta", "1");
        // O limite por usuário é coberto nos testes do Compartilhado; aqui o volume é de setup/concorrência.
        builder.UseSetting("LimiteTaxa:RequisicoesPorSegundo", "10000");
        builder.ConfigureTestServices(servicos => servicos.ValidarTokensComChaveDeTeste());
        ajustar?.Invoke(builder);
    }

    /// <summary>Cliente HTTP com um token de teste do papel informado.</summary>
    public HttpClient ClienteComPapel(string papel)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokensTeste.Emitir(sub: papel, papeis: [papel]));
        return cliente;
    }

    /// <summary>Cria uma conta exclusiva do teste (as contas demo são compartilhadas entre os testes).</summary>
    public async Task<string> CriarContaAsync(SituacaoConta situacao = SituacaoConta.Ativa)
    {
        var id = "CT-" + Guid.NewGuid().ToString("N")[..12];
        await ExecutarNoBancoAsync(async contexto =>
        {
            contexto.Contas.Add(Conta.Criar(id, "Titular de Teste", situacao));
            await contexto.SaveChangesAsync();
        });
        return id;
    }

    public async Task<T> ExecutarNoBancoAsync<T>(Func<LancamentosDbContext, Task<T>> acao)
    {
        ArgumentNullException.ThrowIfNull(acao);
        await using var escopo = Services.CreateAsyncScope();
        return await acao(escopo.ServiceProvider.GetRequiredService<LancamentosDbContext>());
    }

    public Task ExecutarNoBancoAsync(Func<LancamentosDbContext, Task> acao) =>
        ExecutarNoBancoAsync(async contexto =>
        {
            await acao(contexto);
            return true;
        });
}
