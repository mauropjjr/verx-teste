using Banco.Aplicacao.Consolidado.Portas;
using Banco.Dominio.Consolidado;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Banco.Infraestrutura.Consolidado.Persistencia;

/// <summary>Transação do <see cref="ConsolidadoDbContext"/>; a escrita SQL dos repositórios do escopo entra nela.</summary>
public sealed class UnidadeDeTrabalhoConsolidado(ConsolidadoDbContext contexto) : IUnidadeDeTrabalhoConsolidado
{
    public async Task<ITransacaoConsolidado> IniciarAsync(CancellationToken cancelamento) =>
        new Transacao(await contexto.Database.BeginTransactionAsync(cancelamento));

    private sealed class Transacao(IDbContextTransaction transacao) : ITransacaoConsolidado
    {
        public Task ConfirmarAsync(CancellationToken cancelamento) => transacao.CommitAsync(cancelamento);

        // Sem COMMIT, o descarte faz ROLLBACK.
        public ValueTask DisposeAsync() => transacao.DisposeAsync();
    }
}

/// <summary>
/// Inbox (design D7): <c>INSERT ... ON CONFLICT DO NOTHING</c> pela PK. Duas entregas simultâneas da mesma mensagem
/// serializam na PK: a segunda espera o COMMIT da primeira e vê o conflito.
/// </summary>
public sealed class RegistroMensagensProcessadas(ConsolidadoDbContext contexto) : IRegistroMensagensProcessadas
{
    public async Task<bool> RegistrarAsync(Guid idMensagem, CancellationToken cancelamento) =>
        await contexto.Database.ExecuteSqlAsync(
            $"INSERT INTO mensagens_processadas (id_mensagem) VALUES ({idMensagem}) ON CONFLICT (id_mensagem) DO NOTHING",
            cancelamento) == 1;
}

/// <summary>Projeção do consolidado por SQL: só somas comutativas e máximo, atômicos no banco (design D7).</summary>
public sealed class RepositorioResumosDiarios(ConsolidadoDbContext contexto) : IRepositorioResumosDiarios
{
    public async Task SomarAsync(ResumoDiario delta, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(delta);
        await contexto.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO resumos_diarios (conta_id, dia, creditos, debitos)
            VALUES ({delta.ContaId}, {delta.Dia}, {delta.Creditos}, {delta.Debitos})
            ON CONFLICT (conta_id, dia) DO UPDATE
               SET creditos = resumos_diarios.creditos + EXCLUDED.creditos,
                   debitos = resumos_diarios.debitos + EXCLUDED.debitos
            """,
            cancelamento);
    }

    public async Task RegistrarMovimentoAsync(Movimento movimento, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(movimento);
        var tipo = movimento.Natureza == NaturezaMovimento.Credito ? LancamentoProjetado.Credito : LancamentoProjetado.Debito;
        await contexto.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO lancamentos_projetados (id, conta_id, tipo, valor, ocorrido_em)
            VALUES ({movimento.Id}, {movimento.ContaId}, {tipo}, {movimento.Valor}, {movimento.OcorridoEm})
            """,
            cancelamento);
    }

    public async Task AvancarMarcaAsync(string contaId, DateTimeOffset ocorridoEm, DateTimeOffset processadoEm, CancellationToken cancelamento)
    {
        await contexto.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO marcas_atualizacao (conta_id, ultimo_ocorrido_em, processado_em)
            VALUES ({contaId}, {ocorridoEm}, {processadoEm})
            ON CONFLICT (conta_id) DO UPDATE
               SET ultimo_ocorrido_em = GREATEST(marcas_atualizacao.ultimo_ocorrido_em, EXCLUDED.ultimo_ocorrido_em),
                   processado_em = EXCLUDED.processado_em
            """,
            cancelamento);
    }
}
