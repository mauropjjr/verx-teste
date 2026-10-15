using System.Diagnostics;
using Banco.Aplicacao.Lancamentos;
using Banco.Aplicacao.Lancamentos.Portas;
using Banco.Dominio.Lancamentos;
using Banco.Infraestrutura.Lancamentos.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Banco.Infraestrutura.Lancamentos.Inicializacao;

/// <summary>
/// Seed idempotente de demonstração (design D13). Cria as 5 contas que ainda não existem (nunca altera uma conta
/// já gravada) e, com <see cref="OpcoesSemeadura.HistoricoHabilitado"/>, ~1 ano de lançamentos retroativos
/// (<see cref="PlanoHistorico"/>) pelo caso de uso <see cref="RegistrarHistoricoLancamentos"/> — caminho de domínio
/// com evento no outbox, então o consolidado é populado pelo fluxo normal (outbox → RabbitMQ → worker).
/// <para>Idempotência: o histórico inteiro e a <see cref="MarcaSemeadura"/> entram numa única transação (os lotes
/// de <see cref="LancamentosPorLote"/> só limitam o tamanho de cada envio ao banco). Ou a marca está gravada e a
/// etapa é pulada, ou nada do histórico foi gravado e ele é refeito do zero; uma corrida entre duas réplicas
/// termina na UNIQUE da chave de idempotência e o inicializador tenta de novo, já encontrando a marca.</para>
/// </summary>
public sealed partial class SemeadorLancamentos(
    LancamentosDbContext contexto,
    IRepositorioContas contas,
    IUnidadeDeTrabalhoLancamentos unidadeDeTrabalho,
    RegistrarHistoricoLancamentos registrarHistorico,
    TimeProvider relogio,
    OpcoesSemeadura opcoes,
    ILogger<SemeadorLancamentos> logger)
{
    public const string MarcaHistorico = "historico-lancamentos-v1";
    public const int LancamentosPorLote = 1_000;

    public static readonly IReadOnlyList<(string Id, string Titular)> ContasDemo =
    [
        ("CC-0001", "Maria Silva"),
        ("CC-0002", "João Souza"),
        ("CC-0003", "Ana Pereira"),
        ("CC-0004", "Carlos Oliveira"),
        ("CC-0005", "Beatriz Santos"),
    ];

    public async Task SemearAsync(CancellationToken cancelamento)
    {
        await SemearContasAsync(cancelamento);
        if (opcoes.HistoricoHabilitado)
        {
            await SemearHistoricoAsync(cancelamento);
        }
    }

    private async Task SemearContasAsync(CancellationToken cancelamento)
    {
        foreach (var (id, titular) in ContasDemo)
        {
            if (await contas.ObterAsync(id, cancelamento) is null)
            {
                contas.Adicionar(Conta.Criar(id, titular));
            }
        }

        if (await unidadeDeTrabalho.ConfirmarAsync(cancelamento) != ResultadoConfirmacao.Confirmado)
        {
            throw new InvalidOperationException("Falha inesperada ao gravar as contas de demonstração.");
        }
    }

    private async Task SemearHistoricoAsync(CancellationToken cancelamento)
    {
        if (await contexto.Semeaduras.AnyAsync(s => s.Nome == MarcaHistorico, cancelamento))
        {
            return;
        }

        var cronometro = Stopwatch.StartNew();
        var agora = relogio.GetUtcNow();
        try
        {
            await using var transacao = await contexto.Database.BeginTransactionAsync(cancelamento);
            var quantidade = await registrarHistorico.ExecutarAsync(
                PlanoHistorico.Gerar([.. ContasDemo.Select(c => c.Id)], agora), LancamentosPorLote, cancelamento);
            contexto.Semeaduras.Add(new MarcaSemeadura { Nome = MarcaHistorico, ConcluidaEm = agora });
            await contexto.SaveChangesAsync(cancelamento);
            await transacao.CommitAsync(cancelamento);
            LogHistoricoSemeado(logger, quantidade, ContasDemo.Count, cronometro.Elapsed);
        }
        finally
        {
            // Milhares de entidades rastreadas não servem a mais ninguém no escopo.
            contexto.ChangeTracker.Clear();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Histórico de demonstração semeado: {Quantidade} lançamentos em {Contas} contas ({Duracao})")]
    private static partial void LogHistoricoSemeado(ILogger logger, int quantidade, int contas, TimeSpan duracao);
}
