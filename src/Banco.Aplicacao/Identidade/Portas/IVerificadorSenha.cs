using Banco.Dominio.Identidade;

namespace Banco.Aplicacao.Identidade.Portas;

/// <summary>Confere a senha informada contra o hash guardado do usuário (hash forte com salt na Infraestrutura).</summary>
public interface IVerificadorSenha
{
    bool Verificar(Usuario usuario, string senha);
}
