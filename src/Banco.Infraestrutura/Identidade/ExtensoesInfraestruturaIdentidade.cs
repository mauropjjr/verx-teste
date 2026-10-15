using Banco.Aplicacao.Identidade;
using Banco.Aplicacao.Identidade.Portas;
using Banco.Infraestrutura.Compartilhado.Autenticacao;
using Banco.Infraestrutura.Identidade.Inicializacao;
using Banco.Infraestrutura.Identidade.Persistencia;
using Banco.Infraestrutura.Identidade.Senhas;
using Banco.Infraestrutura.Identidade.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Banco.Infraestrutura.Identidade;

/// <summary>
/// Composição do contexto Identidade (design D2): persistência (Postgres, connection string <c>Identidade</c>),
/// hash de senha, chaves RSA (<c>Identidade:Chaves</c>), emissor de JWT, seed (<c>Identidade:UsuariosDemo</c>)
/// e o caso de uso de login.
/// </summary>
public static class ExtensoesInfraestruturaIdentidade
{
    public const string NomeStringConexao = "Identidade";

    public static IServiceCollection AdicionarInfraestruturaIdentidade(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos.AddDbContext<IdentidadeDbContext>(opcoes => opcoes.UseNpgsql(
            configuracao.GetConnectionString(NomeStringConexao)
                ?? throw new InvalidOperationException($"ConnectionStrings:{NomeStringConexao} não configurada.")));
        servicos.AddScoped<IRepositorioUsuarios, RepositorioUsuarios>();
        servicos.AddScoped<IUnidadeDeTrabalhoIdentidade, UnidadeDeTrabalhoIdentidade>();
        servicos.AddHealthChecks().AddCheck<VerificacaoBancoIdentidade>("postgres-identidade");

        servicos.TryAddSingleton(TimeProvider.System);
        servicos.AddSingleton<VerificadorSenha>();
        servicos.AddSingleton<IVerificadorSenha>(sp => sp.GetRequiredService<VerificadorSenha>());
        servicos.AddSingleton(_ => new ProvedorChavesRsa(
            configuracao.GetSection(OpcoesChavesIdentidade.Secao).Get<OpcoesChavesIdentidade>() ?? new OpcoesChavesIdentidade()));
        servicos.AddSingleton<IEmissorToken>(sp =>
        {
            var autenticacao = configuracao.GetSection(ExtensoesAutenticacao.SecaoConfiguracao);
            return new EmissorTokenJwt(
                sp.GetRequiredService<ProvedorChavesRsa>(),
                sp.GetRequiredService<TimeProvider>(),
                autenticacao["Emissor"] ?? ExtensoesAutenticacao.EmissorPadrao,
                autenticacao["Audiencia"] ?? ExtensoesAutenticacao.AudienciaPadrao);
        });

        servicos.AddSingleton(_ =>
            configuracao.GetSection(OpcoesUsuariosDemo.Secao).Get<OpcoesUsuariosDemo>() ?? new OpcoesUsuariosDemo());
        servicos.AddScoped<SemeadorIdentidade>();
        servicos.AddScoped<AutenticarUsuario>();
        return servicos;
    }
}
