using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Banco.Aplicacao.Lancamentos;
using Banco.Dominio.Lancamentos;

namespace Banco.Infraestrutura.Lancamentos.Inicializacao;

/// <summary>
/// Plano determinístico do histórico de demonstração: para cada conta, de 2 a 6 lançamentos por dia nos 365 dias
/// anteriores a hoje (UTC), em horário comercial, e cerca de 2% deles estornados 30 minutos depois. Valores,
/// tipos, descrições e chaves de idempotência dependem só da semente fixa e da posição (conta, dia, ordem); as
/// datas são relativas ao dia da execução.
/// </summary>
internal static class PlanoHistorico
{
    public const int Dias = 365;
    private const int Semente = 20_261_012;
    private const int PercentualEstornos = 2;

    private static readonly string[] _descricoesCredito = ["Salário", "Transferência recebida", "Pix recebido", "Depósito", "Reembolso"];
    private static readonly string[] _descricoesDebito =
        ["Supermercado", "Farmácia", "Combustível", "Conta de luz", "Restaurante", "Pix enviado", "Assinatura de streaming", "Transporte"];

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Dados de demonstração: precisam ser reproduzíveis, não imprevisíveis.")]
    public static IEnumerable<LancamentoHistorico> Gerar(IReadOnlyList<string> contas, DateTimeOffset agora)
    {
        var hoje = new DateTimeOffset(agora.UtcDateTime.Date, TimeSpan.Zero);
        for (var c = 0; c < contas.Count; c++)
        {
            var conta = contas[c];
            var aleatorio = new Random(Semente + c);
            for (var diasAtras = Dias; diasAtras >= 1; diasAtras--)
            {
                var dia = hoje.AddDays(-diasAtras);
                var quantidade = aleatorio.Next(2, 7);
                // Minutos desde as 08:00, em ordem, para o dia ter uma sequência plausível.
                var minutos = Enumerable.Range(0, quantidade).Select(_ => aleatorio.Next(0, 12 * 60)).Order().ToArray();
                for (var i = 0; i < quantidade; i++)
                {
                    var credito = aleatorio.Next(100) < 40;
                    var centavos = credito ? aleatorio.Next(20_000, 500_000) : aleatorio.Next(1_000, 90_000);
                    var descricoes = credito ? _descricoesCredito : _descricoesDebito;
                    var chave = string.Create(CultureInfo.InvariantCulture, $"semeadura-{conta}-{diasAtras:000}-{i}");
                    var ocorridoEm = dia.AddHours(8).AddMinutes(minutos[i]);

                    yield return new LancamentoHistorico(
                        conta,
                        chave,
                        ocorridoEm,
                        credito ? TipoLancamento.Credito : TipoLancamento.Debito,
                        centavos / 100m,
                        descricoes[aleatorio.Next(descricoes.Length)],
                        ChaveOriginal: null);

                    if (aleatorio.Next(100) < PercentualEstornos)
                    {
                        yield return new LancamentoHistorico(conta, chave + "-estorno", ocorridoEm.AddMinutes(30), default, 0, string.Empty, ChaveOriginal: chave);
                    }
                }
            }
        }
    }
}
