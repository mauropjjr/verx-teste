namespace Banco.Dominio.Identidade;

/// <summary>
/// Usuário da plataforma (design D10). Guarda só o hash da senha; a verificação do hash fica fora do domínio
/// e o resultado entra em <see cref="RegistrarTentativaLogin"/>, que aplica a regra de bloqueio:
/// <see cref="LimiteFalhasConsecutivas"/> falhas consecutivas bloqueiam por <see cref="DuracaoBloqueio"/>.
/// </summary>
public sealed class Usuario
{
    public const int LimiteFalhasConsecutivas = 5;
    public static readonly TimeSpan DuracaoBloqueio = TimeSpan.FromMinutes(15);

    private readonly List<string> _papeis = [];

    private Usuario(Guid id, string login, string nome, string hashSenha)
    {
        Id = id;
        Login = login;
        Nome = nome;
        HashSenha = hashSenha;
    }

    public Guid Id { get; private set; }

    /// <summary>Login normalizado (sem espaços nas pontas, minúsculo).</summary>
    public string Login { get; private set; }

    public string Nome { get; private set; }

    public string HashSenha { get; private set; }

    public IReadOnlyList<string> Papeis => _papeis;

    public int TentativasFalhas { get; private set; }

    public DateTimeOffset? BloqueadoAte { get; private set; }

    public static Usuario Criar(Guid id, string login, string nome, string hashSenha, IEnumerable<string> papeis)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        ArgumentException.ThrowIfNullOrWhiteSpace(hashSenha);
        ArgumentNullException.ThrowIfNull(papeis);

        var usuario = new Usuario(id, NormalizarLogin(login), nome.Trim(), hashSenha);
        usuario._papeis.AddRange(papeis.Distinct(StringComparer.Ordinal));
        return usuario;
    }

    public static string NormalizarLogin(string login)
    {
        ArgumentNullException.ThrowIfNull(login);
        return login.Trim().ToLowerInvariant();
    }

    public bool EstaBloqueado(DateTimeOffset agora) => BloqueadoAte > agora;

    /// <summary>
    /// Registra uma tentativa de login e diz se ela é aceita. Bloqueado ⇒ recusa mesmo com a senha certa, sem
    /// prolongar o bloqueio. Falha ⇒ conta; a 5ª consecutiva bloqueia até <c>agora + 15 min</c>. Sucesso ⇒ zera.
    /// </summary>
    public bool RegistrarTentativaLogin(bool senhaConfere, DateTimeOffset agora)
    {
        if (EstaBloqueado(agora))
        {
            return false;
        }

        if (senhaConfere)
        {
            TentativasFalhas = 0;
            BloqueadoAte = null;
            return true;
        }

        TentativasFalhas++;
        if (TentativasFalhas >= LimiteFalhasConsecutivas)
        {
            TentativasFalhas = 0;
            BloqueadoAte = agora + DuracaoBloqueio;
        }

        return false;
    }
}
