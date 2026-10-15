namespace Banco.TestesIntegracao.Identidade.Infra;

/// <summary>Relógio controlado pelo teste (começa no instante real, para os tokens valerem de verdade).</summary>
public sealed class RelogioAjustavel : TimeProvider
{
    private DateTimeOffset _agora = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _agora;

    public void Avancar(TimeSpan intervalo) => _agora += intervalo;
}
