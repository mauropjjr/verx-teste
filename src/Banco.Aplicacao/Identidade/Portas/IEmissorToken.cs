using Banco.Dominio.Identidade;

namespace Banco.Aplicacao.Identidade.Portas;

/// <summary>Emite o token de acesso de um usuário autenticado (implementado na Infraestrutura com JWT RS256).</summary>
public interface IEmissorToken
{
    TokenEmitido Emitir(Usuario usuario);
}

/// <summary>Token de acesso emitido e o instante em que expira.</summary>
public sealed record TokenEmitido(string TokenAcesso, DateTimeOffset ExpiraEm);
