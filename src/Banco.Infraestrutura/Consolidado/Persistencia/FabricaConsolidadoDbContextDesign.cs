using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Banco.Infraestrutura.Consolidado.Persistencia;

/// <summary>Usada só pelo <c>dotnet ef</c> (gerar migrations), sem subir o host nem conectar ao banco.</summary>
internal sealed class FabricaConsolidadoDbContextDesign : IDesignTimeDbContextFactory<ConsolidadoDbContext>
{
    public ConsolidadoDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ConsolidadoDbContext>()
            .UseNpgsql("Host=localhost;Database=consolidado")
            .Options);
}
