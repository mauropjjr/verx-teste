using Banco.Aplicacao.Lancamentos;
using Banco.Aplicacao.Lancamentos.Portas;
using Banco.Infraestrutura.Compartilhado.Mensageria;
using Banco.Infraestrutura.Lancamentos.Inicializacao;
using Banco.Infraestrutura.Lancamentos.Outbox;
using Banco.Infraestrutura.Lancamentos.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Banco.Infraestrutura.Lancamentos;

/// <summary>
/// Composição do contexto Lançamentos (design D2): persistência (Postgres, connection string <c>Lancamentos</c>),
/// repositórios + unidade de trabalho (D2.1), caixa de saída do outbox (D5), consulta do extrato, seed das contas demo e os casos de uso.
/// </summary>
public static class ExtensoesInfraestruturaLancamentos
{
    public const string NomeStringConexao = "Lancamentos";

    public static IServiceCollection AdicionarInfraestruturaLancamentos(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos.AddDbContext<LancamentosDbContext>(opcoes => opcoes.UseNpgsql(
            configuracao.GetConnectionString(NomeStringConexao)
                ?? throw new InvalidOperationException($"ConnectionStrings:{NomeStringConexao} não configurada.")));
        servicos.AddScoped<IRepositorioContas, RepositorioContas>();
        servicos.AddScoped<IRepositorioLancamentos, RepositorioLancamentos>();
        servicos.AddScoped<IUnidadeDeTrabalhoLancamentos, UnidadeDeTrabalhoLancamentos>();
        servicos.AddScoped<IConsultasExtrato, ConsultasExtrato>();
        servicos.AddHttpContextAccessor();
        servicos.AddScoped<ICaixaSaidaEventos, CaixaSaidaOutbox>();
        servicos.AddHealthChecks().AddCheck<VerificacaoBancoLancamentos>("postgres-lancamentos");

        servicos.TryAddSingleton(TimeProvider.System);
        servicos.AddSingleton(
            configuracao.GetSection(OpcoesSemeadura.Secao).Get<OpcoesSemeadura>() ?? new OpcoesSemeadura());
        servicos.AddScoped<SemeadorLancamentos>();
        servicos.AddScoped<ListarContas>();
        servicos.AddScoped<RegistrarLancamento>();
        servicos.AddScoped<EstornarLancamento>();
        servicos.AddScoped<RegistrarHistoricoLancamentos>();
        servicos.AddScoped<ConsultarExtrato>();
        return servicos;
    }

    /// <summary>
    /// Publicador do outbox (design D5), registrado só pelo host <c>Banco.Api.Lancamentos</c>: conexão RabbitMQ
    /// (seção <c>RabbitMq</c>) e o <see cref="PublicadorOutbox"/> em segundo plano (seção <c>Outbox</c>).
    /// <para>De propósito, <b>não</b> adiciona health check do RabbitMQ ao <c>/health/ready</c>: o registro de
    /// lançamentos precisa continuar disponível com o broker fora (spec ledger-entries "Publicação garantida de
    /// eventos") — os eventos ficam pendentes no Postgres e saem quando o broker volta. Tirar a Api do
    /// balanceamento por causa do broker derrubaria justamente o que o outbox existe para proteger.</para>
    /// </summary>
    public static IServiceCollection AdicionarPublicadorOutboxLancamentos(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        var rabbitMq = configuracao.GetSection(OpcoesRabbitMq.Secao).Get<OpcoesRabbitMq>() ?? new OpcoesRabbitMq();
        if (configuracao[$"{OpcoesRabbitMq.Secao}:{nameof(OpcoesRabbitMq.NomeCliente)}"] is null)
        {
            rabbitMq.NomeCliente = "lancamentos-api";
        }

        servicos.AddSingleton(new ConexaoRabbitMq(rabbitMq));
        servicos.AddSingleton(
            configuracao.GetSection(OpcoesPublicadorOutbox.Secao).Get<OpcoesPublicadorOutbox>() ?? new OpcoesPublicadorOutbox());
        servicos.TryAddSingleton(TimeProvider.System);
        servicos.AddHostedService<PublicadorOutbox>();
        return servicos;
    }
}
