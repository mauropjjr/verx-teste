using Banco.Dominio.Identidade;

namespace Banco.TestesUnitarios.Identidade;

// Regra de bloqueio do login (design D10): 5 falhas consecutivas bloqueiam por 15 minutos.
public class UsuarioTestes
{
    private static readonly DateTimeOffset _inicio = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private static Usuario NovoUsuario() =>
        Usuario.Criar(Guid.NewGuid(), "operador", "Operador Demo", "hash", ["operador"]);

    [Fact]
    public void Cinco_falhas_consecutivas_bloqueiam_por_15_minutos_mesmo_com_senha_certa()
    {
        var usuario = NovoUsuario();
        for (var i = 0; i < 5; i++)
        {
            Assert.False(usuario.RegistrarTentativaLogin(senhaConfere: false, _inicio.AddMinutes(i)));
        }

        // Bloqueio conta a partir da 5ª falha (09:04): recusa até 09:18:59, aceita a partir de 09:19.
        Assert.False(usuario.RegistrarTentativaLogin(senhaConfere: true, _inicio.AddMinutes(5)));
        Assert.False(usuario.RegistrarTentativaLogin(senhaConfere: true, _inicio.AddMinutes(19).AddSeconds(-1)));
        Assert.True(usuario.RegistrarTentativaLogin(senhaConfere: true, _inicio.AddMinutes(19)));
    }

    [Fact]
    public void Sucesso_zera_a_contagem_falhas_nao_consecutivas_nao_bloqueiam()
    {
        var usuario = NovoUsuario();
        for (var i = 0; i < 4; i++)
        {
            usuario.RegistrarTentativaLogin(senhaConfere: false, _inicio);
        }

        Assert.True(usuario.RegistrarTentativaLogin(senhaConfere: true, _inicio));

        for (var i = 0; i < 4; i++)
        {
            usuario.RegistrarTentativaLogin(senhaConfere: false, _inicio);
        }

        Assert.True(usuario.RegistrarTentativaLogin(senhaConfere: true, _inicio));
    }

    [Fact]
    public void Tentativas_durante_o_bloqueio_nao_prolongam_o_bloqueio()
    {
        var usuario = NovoUsuario();
        for (var i = 0; i < 5; i++)
        {
            usuario.RegistrarTentativaLogin(senhaConfere: false, _inicio);
        }

        usuario.RegistrarTentativaLogin(senhaConfere: false, _inicio.AddMinutes(10));

        Assert.True(usuario.RegistrarTentativaLogin(senhaConfere: true, _inicio.AddMinutes(15)));
    }
}
