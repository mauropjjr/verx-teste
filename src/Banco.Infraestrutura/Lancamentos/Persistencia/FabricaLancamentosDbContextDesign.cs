using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Banco.Infraestrutura.Lancamentos.Persistencia;

/// <summary>Usada só pelo <c>dotnet ef</c> (gerar migrations), sem subir o host nem conectar ao banco.</summary>
internal sealed class FabricaLancamentosDbContextDesign : IDesignTimeDbContextFactory<LancamentosDbContext>
{
    public LancamentosDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LancamentosDbContext>()
            .UseNpgsql("Host=localhost;Database=lancamentos")
            .Options);
}
