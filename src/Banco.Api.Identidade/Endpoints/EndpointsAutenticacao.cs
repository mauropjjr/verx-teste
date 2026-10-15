using Banco.Aplicacao.Identidade;
using Banco.Infraestrutura.Compartilhado.LimiteTaxa;

namespace Banco.Api.Identidade.Endpoints;

/// <summary>
/// <c>POST /api/v1/autenticacao/login</c> (spec access-control "Login com usuário e senha"): anônimo, limitado
/// por IP. Qualquer recusa (usuário inexistente, senha errada, bloqueio) responde 401 com a mesma mensagem.
/// </summary>
internal static partial class EndpointsAutenticacao
{
    public const string MensagemCredenciaisInvalidas = "Login ou senha inválidos.";

    private const int TamanhoMaximoLogin = 100;
    private const int TamanhoMaximoSenha = 200;

    public static IEndpointRouteBuilder MapearEndpointsAutenticacao(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/autenticacao/login", LogarAsync)
            .AllowAnonymous()
            .RequireRateLimiting(PoliticasLimiteTaxa.LoginPorIp)
            .WithName("Login");
        return endpoints;
    }

    private static async Task<IResult> LogarAsync(
        RequisicaoLogin requisicao,
        AutenticarUsuario autenticarUsuario,
        ILoggerFactory loggers,
        CancellationToken cancelamento)
    {
        var erros = Validar(requisicao);
        if (erros.Count > 0)
        {
            return TypedResults.ValidationProblem(erros);
        }

        var token = await autenticarUsuario.ExecutarAsync(new ComandoAutenticar(requisicao.Login!, requisicao.Senha!), cancelamento);
        if (token is null)
        {
            LogLoginRecusado(loggers.CreateLogger(typeof(EndpointsAutenticacao).FullName!));
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Não autorizado",
                detail: MensagemCredenciaisInvalidas);
        }

        return TypedResults.Ok(new RespostaLogin(token.TokenAcesso, "Bearer", token.ExpiraEm));
    }

    private static Dictionary<string, string[]> Validar(RequisicaoLogin requisicao)
    {
        var erros = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(requisicao.Login) || requisicao.Login.Length > TamanhoMaximoLogin)
        {
            erros["login"] = [$"Informe o login (até {TamanhoMaximoLogin} caracteres)."];
        }

        if (string.IsNullOrEmpty(requisicao.Senha) || requisicao.Senha.Length > TamanhoMaximoSenha)
        {
            erros["senha"] = [$"Informe a senha (até {TamanhoMaximoSenha} caracteres)."];
        }

        return erros;
    }

    // Sem login nem senha no log: só o fato (o IP/correlação já vêm do contexto da requisição).
    [LoggerMessage(Level = LogLevel.Warning, Message = "Login recusado")]
    private static partial void LogLoginRecusado(ILogger logger);
}

/// <summary>Corpo do login.</summary>
internal sealed record RequisicaoLogin(string? Login, string? Senha)
{
    // Nunca expor a senha em ToString (logs, depuração).
    public override string ToString() => $"RequisicaoLogin {{ Login = {Login} }}";
}

/// <summary>Resposta do login: <c>tokenAcesso</c>, <c>tipo</c> (<c>Bearer</c>) e <c>expiraEm</c>.</summary>
internal sealed record RespostaLogin(string TokenAcesso, string Tipo, DateTimeOffset ExpiraEm);
