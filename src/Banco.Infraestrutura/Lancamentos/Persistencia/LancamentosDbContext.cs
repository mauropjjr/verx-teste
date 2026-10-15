using Banco.Dominio.Lancamentos;
using Banco.Infraestrutura.Lancamentos.Inicializacao;
using Banco.Infraestrutura.Lancamentos.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Lancamentos.Persistencia;

/// <summary>
/// Banco <c>lancamentos</c> (design D1/D3/D5/D13): <c>contas</c>, <c>lancamentos</c>, <c>mensagens_outbox</c> e <c>semeaduras</c>.
/// Dinheiro em <c>numeric(18,2)</c>; idempotência e estorno único garantidos por índices UNIQUE.
/// </summary>
public sealed class LancamentosDbContext(DbContextOptions<LancamentosDbContext> opcoes) : DbContext(opcoes)
{
    public const string IndiceChaveIdempotencia = "ux_lancamentos_conta_chave_idempotencia";
    public const string IndiceEstornoDe = "ux_lancamentos_estorno_de";

    public DbSet<Conta> Contas => Set<Conta>();

    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();

    public DbSet<MensagemOutbox> MensagensOutbox => Set<MensagemOutbox>();

    public DbSet<MarcaSemeadura> Semeaduras => Set<MarcaSemeadura>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        MapearContas(modelBuilder);
        MapearLancamentos(modelBuilder);
        MapearOutbox(modelBuilder);
        MapearSemeaduras(modelBuilder);
    }

    private static void MapearContas(ModelBuilder modelBuilder)
    {
        var conta = modelBuilder.Entity<Conta>();
        conta.ToTable("contas");
        conta.HasKey(c => c.Id);
        conta.Property(c => c.Id).HasColumnName("id").HasMaxLength(Conta.TamanhoMaximoId).ValueGeneratedNever();
        conta.Property(c => c.Titular).HasColumnName("titular").HasMaxLength(Conta.TamanhoMaximoTitular).IsRequired();
        conta.Property(c => c.Situacao).HasColumnName("situacao").HasMaxLength(10).IsRequired()
            .HasConversion(s => ConversoesLancamentos.ParaTexto(s), t => ConversoesLancamentos.ParaSituacao(t));
        conta.Ignore(c => c.EstaAtiva);
        conta.Ignore(c => c.TitularMascarado);
    }

    private static void MapearLancamentos(ModelBuilder modelBuilder)
    {
        var lancamento = modelBuilder.Entity<Lancamento>();
        lancamento.ToTable("lancamentos");
        lancamento.HasKey(l => l.Id);
        lancamento.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
        lancamento.Property(l => l.ContaId).HasColumnName("conta_id").HasMaxLength(Conta.TamanhoMaximoId).IsRequired();
        lancamento.Property(l => l.Tipo).HasColumnName("tipo").HasMaxLength(10).IsRequired()
            .HasConversion(t => ConversoesLancamentos.ParaTexto(t), t => ConversoesLancamentos.ParaTipo(t));
        lancamento.Property(l => l.Valor).HasColumnName("valor").HasColumnType("numeric(18,2)");
        lancamento.Property(l => l.Descricao).HasColumnName("descricao").HasMaxLength(Lancamento.TamanhoMaximoDescricao).IsRequired();
        lancamento.Property(l => l.OcorridoEm).HasColumnName("ocorrido_em");
        lancamento.Property(l => l.EstornoDe).HasColumnName("estorno_de");
        lancamento.Property(l => l.ChaveIdempotencia).HasColumnName("chave_idempotencia")
            .HasMaxLength(Lancamento.TamanhoMaximoChaveIdempotencia).IsRequired();
        lancamento.Property(l => l.HashRequisicao).HasColumnName("hash_requisicao").HasMaxLength(64).IsFixedLength().IsRequired();
        lancamento.Ignore(l => l.EhEstorno);

        lancamento.HasOne<Conta>().WithMany().HasForeignKey(l => l.ContaId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_lancamentos_contas");
        lancamento.HasOne<Lancamento>().WithOne().HasForeignKey<Lancamento>(l => l.EstornoDe)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_lancamentos_estorno_de");

        // D4: a UNIQUE resolve a corrida de requisições concorrentes com a mesma chave.
        lancamento.HasIndex(l => new { l.ContaId, l.ChaveIdempotencia }).IsUnique().HasDatabaseName(IndiceChaveIdempotencia);
        // Um lançamento é estornado no máximo uma vez.
        lancamento.HasIndex(l => l.EstornoDe).IsUnique().HasDatabaseName(IndiceEstornoDe);
        // Extrato: conta + ordem decrescente de (ocorrido_em, id), a mesma do cursor.
        lancamento.HasIndex(l => new { l.ContaId, l.OcorridoEm, l.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_lancamentos_conta_ocorrido_em_id");
    }

    private static void MapearOutbox(ModelBuilder modelBuilder)
    {
        var mensagem = modelBuilder.Entity<MensagemOutbox>();
        mensagem.ToTable("mensagens_outbox");
        mensagem.HasKey(m => m.Id);
        mensagem.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
        mensagem.Property(m => m.Tipo).HasColumnName("tipo").HasMaxLength(200).IsRequired();
        mensagem.Property(m => m.Conteudo).HasColumnName("conteudo").HasColumnType("jsonb").IsRequired();
        mensagem.Property(m => m.IdCorrelacao).HasColumnName("id_correlacao").HasMaxLength(100);
        mensagem.Property(m => m.OcorridoEm).HasColumnName("ocorrido_em");
        mensagem.Property(m => m.PublicadoEm).HasColumnName("publicado_em");
        mensagem.Property(m => m.Tentativas).HasColumnName("tentativas");
        // O publicador só lê as pendentes, em ordem de ocorrência.
        mensagem.HasIndex(m => m.OcorridoEm).HasFilter("publicado_em IS NULL").HasDatabaseName("ix_mensagens_outbox_pendentes");
    }

    private static void MapearSemeaduras(ModelBuilder modelBuilder)
    {
        var marca = modelBuilder.Entity<MarcaSemeadura>();
        marca.ToTable("semeaduras");
        marca.HasKey(m => m.Nome);
        marca.Property(m => m.Nome).HasColumnName("nome").HasMaxLength(100);
        marca.Property(m => m.ConcluidaEm).HasColumnName("concluida_em");
    }
}
