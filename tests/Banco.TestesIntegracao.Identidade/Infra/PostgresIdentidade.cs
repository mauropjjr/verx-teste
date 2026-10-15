using Testcontainers.PostgreSql;

namespace Banco.TestesIntegracao.Identidade.Infra;

/// <summary>Um PostgreSQL real (Testcontainers) compartilhado pelos testes da coleção <see cref="ColecaoIdentidade"/>.</summary>
public sealed class PostgresIdentidade : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("identidade")
        .Build();

    public string StringConexao => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Nome)]
public sealed class ColecaoIdentidade : ICollectionFixture<PostgresIdentidade>
{
    public const string Nome = "Identidade com Postgres";
}
