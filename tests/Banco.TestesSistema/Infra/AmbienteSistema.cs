namespace Banco.TestesSistema.Infra;

/// <summary>Onde está o ambiente sob teste: URLs e projeto compose, por variável de ambiente com os defaults do README.</summary>
internal static class AmbienteSistema
{
    public const string VariavelHabilitar = "BANCO_TESTES_SISTEMA";

    public static bool Habilitado => Environment.GetEnvironmentVariable(VariavelHabilitar) == "1";

    public static Uri Identidade => Url("BANCO_URL_IDENTIDADE", "http://localhost:5000");

    public static Uri Lancamentos => Url("BANCO_URL_LANCAMENTOS", "http://localhost:5001");

    public static Uri Consolidado => Url("BANCO_URL_CONSOLIDADO", "http://localhost:5002");

    /// <summary>Nome do projeto compose (o <c>name:</c> do docker-compose.yml).</summary>
    public static string ProjetoCompose => Valor("BANCO_COMPOSE_PROJETO", "banco");

    /// <summary>Conta demo usada pelo teste.</summary>
    public static string Conta => Valor("BANCO_TESTES_SISTEMA_CONTA", "CC-0005");

    public static string SenhaOperador => Valor("IDENTIDADE_SENHA_OPERADOR", "Operador@123");

    private static Uri Url(string variavel, string padrao) => new(Valor(variavel, padrao));

    private static string Valor(string variavel, string padrao) =>
        Environment.GetEnvironmentVariable(variavel) is { Length: > 0 } valor ? valor : padrao;
}
