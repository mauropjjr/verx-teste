using Banco.Dominio.Consolidado;

namespace Banco.TestesUnitarios.Consolidado;

// Spec balance-consolidation: "Consolidar lançamentos por dia", "Eventos fora de ordem",
// "Consultar consolidado diário" (dia sem movimento) e "Saldo em instante intradiário"; design D7.
public class CalculadoraSaldoTestes
{
    private const string Conta = "CC-0001";
    private static readonly DateOnly _dia = new(2026, 10, 5);

    [Fact]
    public void Credito_de_100_e_debito_de_30_no_mesmo_dia_resultam_em_saldo_do_dia_e_fechamento_70()
    {
        Movimento[] movimentos = [Credito(100.00m, Instante(_dia, 9)), Debito(30.00m, Instante(_dia, 17))];

        var dia = Assert.Single(CalculadoraSaldo.ConsolidarPeriodo(0m, CalculadoraSaldo.AgregarPorDia(movimentos), _dia, _dia));

        Assert.Equal((_dia, 100.00m, 30.00m, 70.00m, 70.00m), (dia.Dia, dia.Creditos, dia.Debitos, dia.SaldoDoDia, dia.Fechamento));
    }

    [Fact]
    public void Estorno_no_dia_seguinte_mantem_o_fechamento_de_D_em_100_e_zera_o_de_D_mais_1()
    {
        Movimento[] movimentos = [Credito(100.00m, Instante(_dia, 10)), Debito(100.00m, Instante(_dia.AddDays(1), 8))];

        var dias = CalculadoraSaldo.ConsolidarPeriodo(0m, CalculadoraSaldo.AgregarPorDia(movimentos), _dia, _dia.AddDays(1));

        Assert.Equal([100.00m, 0.00m], dias.Select(d => d.Fechamento));
    }

    [Fact]
    public void Ordem_de_chegada_dos_movimentos_nao_altera_o_consolidado()
    {
        Movimento[] naturais = [Credito(40.00m, Instante(_dia, 10)), Debito(15.50m, Instante(_dia.AddDays(1), 11)), Credito(7.25m, Instante(_dia.AddDays(1), 12))];

        var naOrdem = CalculadoraSaldo.ConsolidarPeriodo(0m, CalculadoraSaldo.AgregarPorDia(naturais), _dia, _dia.AddDays(1));
        var invertidos = CalculadoraSaldo.ConsolidarPeriodo(0m, CalculadoraSaldo.AgregarPorDia(naturais.Reverse()), _dia, _dia.AddDays(1));

        Assert.Equal(naOrdem, invertidos);
        Assert.Equal([40.00m, 31.75m], invertidos.Select(d => d.Fechamento));
    }

    [Fact]
    public void Dia_sem_movimento_aparece_com_creditos_e_debitos_zero_e_herda_o_fechamento_do_dia_anterior()
    {
        Movimento[] movimentos = [Credito(80.00m, Instante(_dia, 10)), Debito(30.00m, Instante(_dia.AddDays(2), 10))];

        var dias = CalculadoraSaldo.ConsolidarPeriodo(0m, CalculadoraSaldo.AgregarPorDia(movimentos), _dia, _dia.AddDays(2));

        Assert.Equal(3, dias.Count);
        var semMovimento = dias[1];
        Assert.Equal((_dia.AddDays(1), 0m, 0m, 0m, 80.00m), (semMovimento.Dia, semMovimento.Creditos, semMovimento.Debitos, semMovimento.SaldoDoDia, semMovimento.Fechamento));
        Assert.Equal(50.00m, dias[2].Fechamento);
    }

    [Fact]
    public void Fechamento_do_periodo_parte_do_fechamento_anterior_ao_inicio()
    {
        var dias = CalculadoraSaldo.ConsolidarPeriodo(250.00m, CalculadoraSaldo.AgregarPorDia([Debito(50.00m, Instante(_dia, 10))]), _dia, _dia);

        Assert.Equal(200.00m, Assert.Single(dias).Fechamento);
    }

    [Fact]
    public void Saldo_em_instante_intradiario_soma_ao_fechamento_do_dia_anterior_so_os_lancamentos_ate_o_instante()
    {
        Movimento[] doDia = [Credito(50.00m, Instante(_dia, 10)), Debito(20.00m, Instante(_dia, 15))];

        Assert.Equal(550.00m, CalculadoraSaldo.SaldoEm(500.00m, doDia, Instante(_dia, 12)));
        Assert.Equal(530.00m, CalculadoraSaldo.SaldoEm(500.00m, doDia, Instante(_dia, 15)));
    }

    [Fact]
    public void Movimento_pertence_ao_dia_UTC_do_instante_em_que_ocorreu()
    {
        // 23:30 em São Paulo (-03:00) já é o dia seguinte em UTC.
        var movimento = Credito(10.00m, new DateTimeOffset(2026, 10, 5, 23, 30, 0, TimeSpan.FromHours(-3)));

        Assert.Equal(new DateOnly(2026, 10, 6), movimento.Dia);
        Assert.Equal(new ResumoDiario(Conta, new DateOnly(2026, 10, 6), 10.00m, 0m), ResumoDiario.DoMovimento(movimento));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    public void Movimento_recusa_valor_nao_positivo_ou_com_mais_de_2_casas(decimal valor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Credito(valor, Instante(_dia, 10)));
    }

    private static Movimento Credito(decimal valor, DateTimeOffset ocorridoEm) =>
        Movimento.Criar(Guid.NewGuid(), Conta, NaturezaMovimento.Credito, valor, ocorridoEm);

    private static Movimento Debito(decimal valor, DateTimeOffset ocorridoEm) =>
        Movimento.Criar(Guid.NewGuid(), Conta, NaturezaMovimento.Debito, valor, ocorridoEm);

    private static DateTimeOffset Instante(DateOnly dia, int hora) => new(dia.ToDateTime(new TimeOnly(hora, 0)), TimeSpan.Zero);
}
