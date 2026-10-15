using Testcontainers.PostgreSql;

namespace Banco.TestesIntegracao.Lancamentos.Infra;

/// <summary>Um PostgreSQL real (Testcontainers) compartilhado pelos testes da coleção <see cref="ColecaoLancamentos"/>.</summary>
public sealed class PostgresLancamentos : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("lancamentos")
        .Build();

    public string StringConexao => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Nome)]
public sealed class ColecaoLancamentos : ICollectionFixture<PostgresLancamentos>
{
    public const string Nome = "Lançamentos com Postgres";
}
