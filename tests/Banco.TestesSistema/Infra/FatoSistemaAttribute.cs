namespace Banco.TestesSistema.Infra;

/// <summary>
/// Teste de sistema: roda contra o compose em execução e só quando <c>BANCO_TESTES_SISTEMA=1</c>. Sem a variável,
/// fica <i>Skipped</i> — o <c>dotnet test</c> da solução não depende do ambiente estar no ar.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FatoSistemaAttribute : FactAttribute
{
    public FatoSistemaAttribute()
    {
        if (!AmbienteSistema.Habilitado)
        {
            Skip = $"Teste de sistema: suba o ambiente (docker compose up --build -d) e defina {AmbienteSistema.VariavelHabilitar}=1.";
        }
    }
}
