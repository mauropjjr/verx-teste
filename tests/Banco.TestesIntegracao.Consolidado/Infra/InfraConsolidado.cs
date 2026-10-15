using System.Globalization;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Banco.TestesIntegracao.Consolidado.Infra;

/// <summary>
/// Postgres, RabbitMQ e Redis reais (Testcontainers), subidos em paralelo e compartilhados pelos testes da coleção
/// <see cref="ColecaoConsolidado"/>. Cada teste usa contas próprias, então o estado acumulado não interfere.
/// </summary>
public sealed class InfraConsolidado : IAsyncLifetime
{
    public const string UsuarioRabbit = "banco";
    public const string SenhaRabbit = "banco_teste";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("consolidado")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername(UsuarioRabbit).WithPassword(SenhaRabbit)
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7").Build();

    public string StringConexaoPostgres => _postgres.GetConnectionString();

    public string StringConexaoRabbit => _rabbit.GetConnectionString();

    public string StringConexaoRedis => _redis.GetConnectionString();

    /// <summary>Configuração do host apontando para os containers (mesmas chaves do appsettings do Worker).</summary>
    public Dictionary<string, string?> Configuracao() => new()
    {
        ["ConnectionStrings:Consolidado"] = StringConexaoPostgres,
        ["ConnectionStrings:Redis"] = StringConexaoRedis,
        ["RabbitMq:Host"] = _rabbit.Hostname,
        ["RabbitMq:Porta"] = _rabbit.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort).ToString(CultureInfo.InvariantCulture),
        ["RabbitMq:Usuario"] = UsuarioRabbit,
        ["RabbitMq:Senha"] = SenhaRabbit,
        ["Consumidor:EsperaBaseRetentativa"] = "00:00:00.050",
    };

    public Task InitializeAsync() => Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync(), _redis.StartAsync());

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

[CollectionDefinition(Nome)]
public sealed class ColecaoConsolidado : ICollectionFixture<InfraConsolidado>
{
    public const string Nome = "Consolidado com Postgres, RabbitMQ e Redis";
}
