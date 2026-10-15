using Banco.Dominio.Lancamentos;

namespace Banco.TestesUnitarios.Lancamentos;

// Spec ledger-entries, "Listar contas": o titular nunca sai completo (ex.: "Mar*** S***").
public class ContaTestes
{
    [Theory]
    [InlineData("Maria Silva", "Mar*** S***")]
    [InlineData("  João   da Souza ", "Joã*** d*** S***")]
    [InlineData("Ana", "An***")]
    [InlineData("Bo", "B***")]
    [InlineData("X", "X***")]
    public void Titular_mascarado_mantem_ate_3_letras_do_primeiro_nome_e_a_inicial_dos_demais(string titular, string esperado)
    {
        var conta = Conta.Criar("CC-0001", titular);

        Assert.Equal(esperado, conta.TitularMascarado);
    }

    [Fact]
    public void Conta_criada_esta_ativa()
    {
        var conta = Conta.Criar("CC-0001", "Maria Silva");

        Assert.Equal(SituacaoConta.Ativa, conta.Situacao);
        Assert.True(conta.EstaAtiva);
    }
}
