using Banco.Aplicacao.Consolidado;
using Banco.Aplicacao.Consolidado.Portas;
using Banco.Infraestrutura.Compartilhado.Mensageria;
using Banco.Infraestrutura.Consolidado.Cache;
using Banco.Infraestrutura.Consolidado.Mensageria;
using Banco.Infraestrutura.Consolidado.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Banco.Infraestrutura.Consolidado;

/// <summary>
/// Composição do contexto Consolidado (design D2): persistência (Postgres, connection string <c>Consolidado</c>),
/// repositórios da projeção + unidade de trabalho (D2.1/D7), Redis (connection string <c>Redis</c>, D9) e o caso de
/// uso <see cref="ProjetarLancamento"/>.
/// </summary>
public static class ExtensoesInfraestruturaConsolidado
{
    public const string NomeStringConexao = "Consolidado";
    public const string NomeStringConexaoRedis = "Redis";

    public static IServiceCollection AdicionarInfraestruturaConsolidado(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos.AddDbContext<ConsolidadoDbContext>(opcoes => opcoes.UseNpgsql(
            configuracao.GetConnectionString(NomeStringConexao)
                ?? throw new InvalidOperationException($"ConnectionStrings:{NomeStringConexao} não configurada.")));
        servicos.AddScoped<IUnidadeDeTrabalhoConsolidado, UnidadeDeTrabalhoConsolidado>();
        servicos.AddScoped<IRegistroMensagensProcessadas, RegistroMensagensProcessadas>();
        servicos.AddScoped<IRepositorioResumosDiarios, RepositorioResumosDiarios>();
        servicos.AddHealthChecks().AddCheck<VerificacaoBancoConsolidado>("postgres-consolidado");

        var redis = configuracao.GetConnectionString(NomeStringConexaoRedis)
            ?? throw new InvalidOperationException($"ConnectionStrings:{NomeStringConexaoRedis} não configurada.");
        servicos.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            var opcoes = ConfigurationOptions.Parse(redis);
            // Sobe e segue com o Redis fora (o cache é acessório): reconecta sozinho em segundo plano. Timeouts curtos e
            // FailFast (sem fila de comandos enquanto desconectado): Redis fora falha na hora e a consulta vai ao Postgres.
            opcoes.AbortOnConnectFail = false;
            opcoes.ConnectTimeout = 1_000;
            opcoes.SyncTimeout = 500;
            opcoes.AsyncTimeout = 500;
            opcoes.BacklogPolicy = BacklogPolicy.FailFast;
            return ConnectionMultiplexer.Connect(opcoes);
        });
        servicos.AddSingleton<ISinalizadorVersaoConta, SinalizadorVersaoContaRedis>();

        servicos.TryAddSingleton(TimeProvider.System);
        servicos.AddScoped<ProjetarLancamento>();
        return servicos;
    }

    /// <summary>
    /// Lado de leitura do consolidado (design D2.1/D7), registrado só pelo host <c>Banco.Api.Consolidado</c>: consultas
    /// SQL atrás do cache-aside versionado no Redis com circuit breaker (D9), a verificação de prontidão do Redis e os
    /// casos de uso de consulta.
    /// </summary>
    public static IServiceCollection AdicionarConsultasConsolidado(this IServiceCollection servicos)
    {
        servicos.AddScoped<ConsultasConsolidado>();
        servicos.AddSingleton<CircuitoCacheConsolidado>();
        servicos.AddScoped<IConsultasConsolidado, ConsultasConsolidadoEmCache>();
        servicos.AddHealthChecks().AddCheck<VerificacaoRedisConsolidado>("redis");
        servicos.TryAddSingleton(TimeProvider.System);
        servicos.AddScoped<ConsultarConsolidadoDiario>();
        servicos.AddScoped<ConsultarSaldo>();
        return servicos;
    }

    /// <summary>
    /// Consumidor de <c>lancamento.registrado.v1</c> (design D6/D8), registrado só pelo host
    /// <c>Banco.Worker.Consolidado</c>: conexão RabbitMQ (seção <c>RabbitMq</c>), opções de consumo/retry (seção
    /// <c>Consumidor</c>), o processador da mensagem e o <see cref="ConsumidorLancamentosRegistrados"/> em segundo
    /// plano, cujo canal aberto entra na verificação de saúde do Worker.
    /// </summary>
    public static IServiceCollection AdicionarConsumidorConsolidado(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos.AddSingleton(new ConexaoRabbitMq(LerOpcoesRabbitMq(configuracao, "consolidado-worker")));
        servicos.AddSingleton(
            configuracao.GetSection(OpcoesConsumidorConsolidado.Secao).Get<OpcoesConsumidorConsolidado>() ?? new OpcoesConsumidorConsolidado());
        servicos.AddSingleton<ProcessadorMensagemLancamento>();
        servicos.TryAddSingleton(TimeProvider.System);
        servicos.AddSingleton<ConsumidorLancamentosRegistrados>();
        servicos.AddHostedService(sp => sp.GetRequiredService<ConsumidorLancamentosRegistrados>());
        servicos.AddHealthChecks().AddCheck<VerificacaoConsumidorConsolidado>("rabbitmq-consumidor");
        return servicos;
    }

    /// <summary>
    /// Reprocessamento da DLQ (design D8), registrado só pelo host <c>Banco.Api.Consolidado</c>: conexão RabbitMQ (seção
    /// <c>RabbitMq</c>), aberta só quando um admin pede o reprocessamento — o broker fora não afeta as consultas nem a
    /// prontidão da Api.
    /// </summary>
    public static IServiceCollection AdicionarReprocessamentoDlqConsolidado(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        servicos.AddSingleton(new ConexaoRabbitMq(LerOpcoesRabbitMq(configuracao, "consolidado-api")));
        servicos.AddSingleton<IFilaMensagensMortas, FilaMensagensMortasRabbitMq>();
        servicos.AddScoped<ReprocessarDlq>();
        return servicos;
    }

    private static OpcoesRabbitMq LerOpcoesRabbitMq(IConfiguration configuracao, string nomeClientePadrao)
    {
        var opcoes = configuracao.GetSection(OpcoesRabbitMq.Secao).Get<OpcoesRabbitMq>() ?? new OpcoesRabbitMq();
        if (configuracao[$"{OpcoesRabbitMq.Secao}:{nameof(OpcoesRabbitMq.NomeCliente)}"] is null)
        {
            opcoes.NomeCliente = nomeClientePadrao;
        }

        return opcoes;
    }
}
