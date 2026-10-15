namespace Banco.Infraestrutura.Identidade.Inicializacao;

/// <summary>
/// Senhas dos usuários de demonstração criados pelo seed (seção <c>Identidade:UsuariosDemo</c>, design D10/D13).
/// Os defaults servem só para o ambiente local e são documentados no README; sobrescreva por variável
/// (ex.: <c>Identidade__UsuariosDemo__SenhaAdmin</c>).
/// </summary>
public sealed class OpcoesUsuariosDemo
{
    public const string Secao = "Identidade:UsuariosDemo";

    public string SenhaConsulta { get; set; } = "Consulta@123";

    public string SenhaOperador { get; set; } = "Operador@123";

    public string SenhaAdmin { get; set; } = "Admin@123";
}
