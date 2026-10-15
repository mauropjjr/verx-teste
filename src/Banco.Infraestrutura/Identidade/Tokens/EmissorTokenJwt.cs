using Banco.Aplicacao.Identidade.Portas;
using Banco.Dominio.Identidade;
using Banco.Infraestrutura.Compartilhado.Autenticacao;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Banco.Infraestrutura.Identidade.Tokens;

/// <summary>
/// Emite o token de acesso JWT RS256 (design D10): <c>iss=banco-identidade</c>, <c>aud=banco-api</c>,
/// <c>sub</c> (id do usuário), <c>name</c>, uma claim <c>role</c> por papel, <c>kid</c> no header e validade de
/// <see cref="Validade"/>. Emissor/audiência são os mesmos que as Apis validam (<see cref="ExtensoesAutenticacao"/>).
/// </summary>
public sealed class EmissorTokenJwt(
    ProvedorChavesRsa chaves,
    TimeProvider relogio,
    string emissor = ExtensoesAutenticacao.EmissorPadrao,
    string audiencia = ExtensoesAutenticacao.AudienciaPadrao) : IEmissorToken
{
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30);

    private readonly JsonWebTokenHandler _manipulador = new() { SetDefaultTimesOnTokenCreation = false };

    public TokenEmitido Emitir(Usuario usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var agora = relogio.GetUtcNow();
        var expiraEm = agora + Validade;

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = emissor,
            Audience = audiencia,
            IssuedAt = agora.UtcDateTime,
            NotBefore = agora.UtcDateTime,
            Expires = expiraEm.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = usuario.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                ["name"] = usuario.Nome,
                ["role"] = usuario.Papeis.ToArray(),
            },
            SigningCredentials = new SigningCredentials(chaves.Chave, SecurityAlgorithms.RsaSha256),
        };

        return new TokenEmitido(_manipulador.CreateToken(descritor), expiraEm);
    }
}
