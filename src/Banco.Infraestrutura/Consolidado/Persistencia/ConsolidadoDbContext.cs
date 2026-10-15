using Banco.Dominio.Consolidado;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Consolidado.Persistencia;

/// <summary>
/// Banco <c>consolidado</c> (design D1/D7): <c>resumos_diarios</c> (deltas por conta e dia), <c>lancamentos_projetados</c>
/// (saldo intradiário), <c>marcas_atualizacao</c> (<c>atualizadoAte</c>) e <c>mensagens_processadas</c> (inbox).
/// A escrita é feita por SQL (UPSERT atômico) nos repositórios; o modelo EF existe para as migrations e leituras.
/// </summary>
public sealed class ConsolidadoDbContext(DbContextOptions<ConsolidadoDbContext> opcoes) : DbContext(opcoes)
{
    public DbSet<LinhaResumoDiario> ResumosDiarios => Set<LinhaResumoDiario>();

    public DbSet<LancamentoProjetado> LancamentosProjetados => Set<LancamentoProjetado>();

    public DbSet<MarcaAtualizacao> MarcasAtualizacao => Set<MarcaAtualizacao>();

    public DbSet<MensagemProcessada> MensagensProcessadas => Set<MensagemProcessada>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var resumo = modelBuilder.Entity<LinhaResumoDiario>();
        resumo.ToTable("resumos_diarios");
        // A PK (conta_id, dia) é o alvo do ON CONFLICT e serve à soma dos dias <= D (fechamento).
        resumo.HasKey(r => new { r.ContaId, r.Dia }).HasName("pk_resumos_diarios");
        resumo.Property(r => r.ContaId).HasColumnName("conta_id").HasMaxLength(Movimento.TamanhoMaximoContaId);
        resumo.Property(r => r.Dia).HasColumnName("dia");
        resumo.Property(r => r.Creditos).HasColumnName("creditos").HasColumnType("numeric(18,2)");
        resumo.Property(r => r.Debitos).HasColumnName("debitos").HasColumnType("numeric(18,2)");

        var projetado = modelBuilder.Entity<LancamentoProjetado>();
        projetado.ToTable("lancamentos_projetados");
        projetado.HasKey(l => l.Id).HasName("pk_lancamentos_projetados");
        projetado.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
        projetado.Property(l => l.ContaId).HasColumnName("conta_id").HasMaxLength(Movimento.TamanhoMaximoContaId).IsRequired();
        projetado.Property(l => l.Tipo).HasColumnName("tipo").HasMaxLength(10).IsRequired();
        projetado.Property(l => l.Valor).HasColumnName("valor").HasColumnType("numeric(18,2)");
        projetado.Property(l => l.OcorridoEm).HasColumnName("ocorrido_em");
        // Saldo em instante: lançamentos da conta no dia até `em`.
        projetado.HasIndex(l => new { l.ContaId, l.OcorridoEm }).HasDatabaseName("ix_lancamentos_projetados_conta_ocorrido_em");

        var marca = modelBuilder.Entity<MarcaAtualizacao>();
        marca.ToTable("marcas_atualizacao");
        marca.HasKey(m => m.ContaId).HasName("pk_marcas_atualizacao");
        marca.Property(m => m.ContaId).HasColumnName("conta_id").HasMaxLength(Movimento.TamanhoMaximoContaId);
        marca.Property(m => m.UltimoOcorridoEm).HasColumnName("ultimo_ocorrido_em");
        marca.Property(m => m.ProcessadoEm).HasColumnName("processado_em");

        var mensagem = modelBuilder.Entity<MensagemProcessada>();
        mensagem.ToTable("mensagens_processadas");
        mensagem.HasKey(m => m.IdMensagem).HasName("pk_mensagens_processadas");
        mensagem.Property(m => m.IdMensagem).HasColumnName("id_mensagem").ValueGeneratedNever();
        mensagem.Property(m => m.ProcessadoEm).HasColumnName("processado_em").HasDefaultValueSql("now()");
    }
}

/// <summary>Linha de <c>resumos_diarios</c>: deltas de uma conta num dia UTC.</summary>
public sealed class LinhaResumoDiario
{
    public required string ContaId { get; init; }

    public DateOnly Dia { get; init; }

    public decimal Creditos { get; init; }

    public decimal Debitos { get; init; }
}

/// <summary>Linha de <c>lancamentos_projetados</c>: o lançamento individual, para o saldo intradiário.</summary>
public sealed class LancamentoProjetado
{
    public const string Credito = "CREDITO";
    public const string Debito = "DEBITO";

    public Guid Id { get; init; }

    public required string ContaId { get; init; }

    /// <summary><see cref="Credito"/> ou <see cref="Debito"/>.</summary>
    public required string Tipo { get; init; }

    public decimal Valor { get; init; }

    public DateTimeOffset OcorridoEm { get; init; }
}

/// <summary>Linha de <c>marcas_atualizacao</c>: maior <c>ocorridoEm</c> já processado da conta (<c>atualizadoAte</c>).</summary>
public sealed class MarcaAtualizacao
{
    public required string ContaId { get; init; }

    public DateTimeOffset UltimoOcorridoEm { get; init; }

    public DateTimeOffset ProcessadoEm { get; init; }
}

/// <summary>Linha de <c>mensagens_processadas</c> (inbox).</summary>
public sealed class MensagemProcessada
{
    public Guid IdMensagem { get; init; }

    public DateTimeOffset ProcessadoEm { get; init; }
}
