namespace Banco.Infraestrutura.Compartilhado.LimiteTaxa;

/// <summary>Políticas nomeadas de limite de taxa (além do limitador global por usuário).</summary>
public static class PoliticasLimiteTaxa
{
    /// <summary>Login: token bucket por IP de origem (design D10), para frear força bruta.</summary>
    public const string LoginPorIp = "login-por-ip";
}
