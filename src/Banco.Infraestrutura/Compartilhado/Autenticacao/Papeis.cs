namespace Banco.Infraestrutura.Compartilhado.Autenticacao;

/// <summary>Papéis emitidos pela Identidade na claim <c>role</c> (design D10).</summary>
public static class Papeis
{
    public const string Consulta = "consulta";
    public const string Operador = "operador";
    public const string Admin = "admin";
}
