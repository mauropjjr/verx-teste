using System.Text.Json;
using Banco.Aplicacao.Compartilhado.Contratos;

namespace Banco.TestesUnitarios.Compartilhado.Contratos;

public class LancamentoRegistradoV1Testes
{
    private static readonly Guid _idLancamento = Guid.Parse("0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5a6b");
    private static readonly Guid _idEstornado = Guid.Parse("0192a3b4-0000-7000-8000-000000000001");

    [Fact]
    public void Serializa_e_desserializa_preservando_decimal_DateTimeOffset_e_nomes_do_contrato()
    {
        var ocorridoEm = new DateTimeOffset(2026, 10, 12, 23, 59, 59, 123, TimeSpan.FromHours(-3));
        var evento = new LancamentoRegistradoV1(
            IdLancamento: _idLancamento,
            ContaId: "CC-0001",
            Tipo: TipoLancamentoV1.Debito,
            Valor: 150.70m,
            OcorridoEm: ocorridoEm,
            EstornoDe: _idEstornado);

        var json = JsonSerializer.Serialize(evento, LancamentoRegistradoV1.OpcoesJson);
        var lido = JsonSerializer.Deserialize<LancamentoRegistradoV1>(json, LancamentoRegistradoV1.OpcoesJson);

        // Formato no fio (contrato com o Consolidado): camelCase, tipo textual, valor numérico exato.
        Assert.Equal(
            "{\"idLancamento\":\"0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5a6b\",\"contaId\":\"CC-0001\",\"tipo\":\"DEBITO\","
            + "\"valor\":150.70,\"ocorridoEm\":\"2026-10-12T23:59:59.123-03:00\","
            + "\"estornoDe\":\"0192a3b4-0000-7000-8000-000000000001\"}",
            json);
        Assert.Equal(evento, lido);
        Assert.Equal("150.70", lido!.Valor.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(TimeSpan.FromHours(-3), lido.OcorridoEm.Offset);
        Assert.Equal(ocorridoEm.UtcTicks, lido.OcorridoEm.UtcTicks);
    }

    [Fact]
    public void Credito_sem_estorno_desserializa_de_json_produzido_externamente()
    {
        const string json = "{\"idLancamento\":\"0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5a6b\",\"contaId\":\"CC-0002\","
            + "\"tipo\":\"CREDITO\",\"valor\":0.01,\"ocorridoEm\":\"2026-01-01T00:00:00+00:00\",\"estornoDe\":null}";

        var lido = JsonSerializer.Deserialize<LancamentoRegistradoV1>(json, LancamentoRegistradoV1.OpcoesJson);

        Assert.NotNull(lido);
        Assert.Equal(TipoLancamentoV1.Credito, lido.Tipo);
        Assert.Equal(0.01m, lido.Valor);
        Assert.Null(lido.EstornoDe);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), lido.OcorridoEm);
    }
}
