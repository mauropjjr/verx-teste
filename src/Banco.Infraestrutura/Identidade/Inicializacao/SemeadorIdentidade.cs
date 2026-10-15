using Banco.Aplicacao.Identidade.Portas;
using Banco.Dominio.Identidade;
using Banco.Infraestrutura.Compartilhado.Autenticacao;
using Banco.Infraestrutura.Identidade.Senhas;

namespace Banco.Infraestrutura.Identidade.Inicializacao;

/// <summary>
/// Seed idempotente dos usuários de demonstração <c>consulta</c>, <c>operador</c> e <c>admin</c> (design D13):
/// cria só os que ainda não existem e nunca altera um usuário já gravado.
/// </summary>
public sealed class SemeadorIdentidade(
    IRepositorioUsuarios repositorio,
    IUnidadeDeTrabalhoIdentidade unidadeDeTrabalho,
    VerificadorSenha senhas,
    OpcoesUsuariosDemo opcoes)
{
    public async Task SemearAsync(CancellationToken cancelamento)
    {
        (string Login, string Nome, string Senha, string Papel)[] demos =
        [
            ("consulta", "Consulta Demo", opcoes.SenhaConsulta, Papeis.Consulta),
            ("operador", "Operador Demo", opcoes.SenhaOperador, Papeis.Operador),
            ("admin", "Admin Demo", opcoes.SenhaAdmin, Papeis.Admin),
        ];

        foreach (var demo in demos)
        {
            if (await repositorio.ObterPorLoginAsync(demo.Login, cancelamento) is null)
            {
                var hash = senhas.GerarHash(demo.Senha);
                repositorio.Adicionar(Usuario.Criar(Guid.CreateVersion7(), demo.Login, demo.Nome, hash, [demo.Papel]));
            }
        }

        await unidadeDeTrabalho.ConfirmarAsync(cancelamento);
    }
}
