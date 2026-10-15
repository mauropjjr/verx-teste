using Banco.Aplicacao.Identidade.Portas;
using Banco.Dominio.Identidade;
using Microsoft.AspNetCore.Identity;

namespace Banco.Infraestrutura.Identidade.Senhas;

/// <summary>
/// Hash de senha com o <see cref="PasswordHasher{TUser}"/> do ASP.NET Core Identity (formato V3:
/// PBKDF2-HMAC-SHA512, 100 mil iterações, salt aleatório) — só o hasher, sem os stores do Identity (design D10).
/// </summary>
public sealed class VerificadorSenha : IVerificadorSenha
{
    private readonly PasswordHasher<Usuario> _hasher = new();

    public string GerarHash(string senha) => _hasher.HashPassword(null!, senha);

    public bool Verificar(Usuario usuario, string senha)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return _hasher.VerifyHashedPassword(usuario, usuario.HashSenha, senha) != PasswordVerificationResult.Failed;
    }
}
