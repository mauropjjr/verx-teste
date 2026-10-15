using Banco.Dominio.Lancamentos;

namespace Banco.TestesUnitarios.Lancamentos;

// Spec ledger-entries: "Registrar lançamento" (valor positivo, até 2 casas, descrição até 140) e
// "Estorno de lançamento" (tipo oposto, mesmo valor, vínculo com o original, estorno não se estorna).
public class LancamentoTestes
{
    private static readonly DateTimeOffset _agora = new(2026, 10, 12, 14, 30, 15, TimeSpan.Zero);

    private static Lancamento Registrar(
        decimal valor = 150.75m,
        string descricao = "Depósito",
        TipoLancamento tipo = TipoLancamento.Credito) =>
        Lancamento.Registrar("CC-0001", tipo, valor, descricao, _agora, "chave-1", "hash-1");

    [Fact]
    public void Registrar_credito_valido_guarda_os_dados_e_gera_id_versao_7()
    {
        var lancamento = Registrar();

        Assert.Equal(7, lancamento.Id.Version);
        Assert.Equal("CC-0001", lancamento.ContaId);
        Assert.Equal(TipoLancamento.Credito, lancamento.Tipo);
        Assert.Equal(150.75m, lancamento.Valor);
        Assert.Equal("Depósito", lancamento.Descricao);
        Assert.Equal(_agora, lancamento.OcorridoEm);
        Assert.Null(lancamento.EstornoDe);
        Assert.Equal("chave-1", lancamento.ChaveIdempotencia);
        Assert.Equal("hash-1", lancamento.HashRequisicao);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10.00")]
    [InlineData("10.001")]
    [InlineData("0.005")]
    public void Valor_zero_negativo_ou_com_mais_de_2_casas_e_rejeitado(string valor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Registrar(valor: decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Valor_com_zeros_a_direita_alem_de_2_casas_e_aceito_porque_nao_muda_o_valor()
    {
        Assert.Equal(10.5m, Registrar(valor: 10.500m).Valor);
    }

    [Fact]
    public void Descricao_com_140_caracteres_e_aceita_e_com_141_ou_vazia_e_rejeitada()
    {
        Assert.Equal(140, Registrar(descricao: new string('a', 140)).Descricao.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => Registrar(descricao: new string('a', 141)));
        Assert.Throws<ArgumentException>(() => Registrar(descricao: "   "));
    }

    [Fact]
    public void Ocorrido_em_e_guardado_em_UTC_com_precisao_de_microssegundos()
    {
        // O Postgres guarda microssegundos: o que a API devolve no 201 tem de ser o mesmo que o extrato lê.
        var comTicks = new DateTimeOffset(2026, 10, 12, 11, 30, 15, TimeSpan.FromHours(-3)).AddTicks(1234567);

        var lancamento = Lancamento.Registrar("CC-0001", TipoLancamento.Debito, 1m, "x", comTicks, "c", "h");

        Assert.Equal(TimeSpan.Zero, lancamento.OcorridoEm.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 14, 30, 15, TimeSpan.Zero).AddTicks(1234560), lancamento.OcorridoEm);
    }

    [Theory]
    [InlineData(TipoLancamento.Credito, TipoLancamento.Debito)]
    [InlineData(TipoLancamento.Debito, TipoLancamento.Credito)]
    public void Estorno_gera_lancamento_de_tipo_oposto_mesmo_valor_e_vinculado_ao_original(TipoLancamento original, TipoLancamento esperado)
    {
        var lancamento = Registrar(valor: 100.00m, tipo: original);
        var depois = _agora.AddMinutes(5);

        var estorno = lancamento.Estornar(depois, "chave-estorno", "hash-estorno");

        Assert.Equal(esperado, estorno.Tipo);
        Assert.Equal(100.00m, estorno.Valor);
        Assert.Equal(lancamento.Id, estorno.EstornoDe);
        Assert.Equal("CC-0001", estorno.ContaId);
        Assert.Equal(depois, estorno.OcorridoEm);
        Assert.NotEqual(lancamento.Id, estorno.Id);
        Assert.Equal("chave-estorno", estorno.ChaveIdempotencia);
        Assert.True(estorno.EhEstorno);
        Assert.False(lancamento.EhEstorno);
        Assert.InRange(estorno.Descricao.Length, 1, Lancamento.TamanhoMaximoDescricao);
    }

    [Fact]
    public void Estorno_nao_pode_ser_estornado()
    {
        var estorno = Registrar().Estornar(_agora, "chave-estorno", "hash-estorno");

        Assert.Throws<InvalidOperationException>(() => estorno.Estornar(_agora, "outra", "outro-hash"));
    }
}
