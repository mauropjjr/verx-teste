using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Banco.Infraestrutura.Identidade.Persistencia;

/// <summary>Usada só pelo <c>dotnet ef</c> (gerar migrations), sem subir o host nem conectar ao banco.</summary>
internal sealed class FabricaIdentidadeDbContextDesign : IDesignTimeDbContextFactory<IdentidadeDbContext>
{
    public IdentidadeDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IdentidadeDbContext>()
            .UseNpgsql("Host=localhost;Database=identidade")
            .Options);
}
