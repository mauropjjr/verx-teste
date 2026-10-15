using Banco.Aplicacao.Identidade.Portas;
using Banco.Dominio.Identidade;

namespace Banco.Aplicacao.Identidade;

/// <summary>
/// Caso de uso do login (spec access-control "Login com usuário e senha"): confere a senha, aplica a regra de
/// bloqueio do <see cref="Usuario"/>, persiste o novo estado das tentativas e emite o token. Devolve
/// <c>null</c> em qualquer recusa — usuário inexistente, senha errada ou bloqueado — para que o chamador
/// responda sempre com a mesma mensagem genérica.
/// </summary>
public sealed class AutenticarUsuario(
    IRepositorioUsuarios repositorio,
    IUnidadeDeTrabalhoIdentidade unidadeDeTrabalho,
    IVerificadorSenha verificadorSenha,
    IEmissorToken emissorToken,
    TimeProvider relogio)
{
    public async Task<TokenEmitido?> ExecutarAsync(ComandoAutenticar comando, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var usuario = await repositorio.ObterPorLoginAsync(comando.Login, cancelamento);
        if (usuario is null)
        {
            return null;
        }

        var senhaConfere = verificadorSenha.Verificar(usuario, comando.Senha);
        var aceito = usuario.RegistrarTentativaLogin(senhaConfere, relogio.GetUtcNow());
        await unidadeDeTrabalho.ConfirmarAsync(cancelamento);

        return aceito ? emissorToken.Emitir(usuario) : null;
    }
}

/// <summary>Credenciais informadas no login.</summary>
public sealed record ComandoAutenticar(string Login, string Senha)
{
    // Nunca expor a senha em ToString (logs, depuração).
    public override string ToString() => $"ComandoAutenticar {{ Login = {Login} }}";
}
