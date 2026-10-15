using Banco.Dominio.Identidade;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Identidade.Persistencia;

/// <summary>Banco <c>identidade</c> (design D1/D10): só a tabela <c>usuarios</c>.</summary>
public sealed class IdentidadeDbContext(DbContextOptions<IdentidadeDbContext> opcoes) : DbContext(opcoes)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var usuario = modelBuilder.Entity<Usuario>();
        usuario.ToTable("usuarios");
        usuario.HasKey(u => u.Id);
        usuario.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
        usuario.Property(u => u.Login).HasColumnName("login").HasMaxLength(100).IsRequired();
        usuario.HasIndex(u => u.Login).IsUnique().HasDatabaseName("ux_usuarios_login");
        usuario.Property(u => u.Nome).HasColumnName("nome").HasMaxLength(200).IsRequired();
        usuario.Property(u => u.HashSenha).HasColumnName("hash_senha").HasMaxLength(512).IsRequired();
        usuario.Property(u => u.Papeis).HasColumnName("papeis").HasField("_papeis").IsRequired();
        usuario.Property(u => u.TentativasFalhas).HasColumnName("tentativas_falhas");
        usuario.Property(u => u.BloqueadoAte).HasColumnName("bloqueado_ate");
    }
}
