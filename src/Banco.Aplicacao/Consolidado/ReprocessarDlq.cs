using Banco.Aplicacao.Consolidado.Portas;

namespace Banco.Aplicacao.Consolidado;

/// <summary>
/// Caso de uso "Reprocessar DLQ" (spec balance-consolidation, design D8): devolve mensagens mortas ao fluxo principal;
/// o inbox da projeção garante que reprocessar é seguro (idempotente).
/// </summary>
public sealed class ReprocessarDlq(IFilaMensagensMortas filaMensagensMortas)
{
    public const int MaximoPadrao = 100;
    public const int MaximoPermitido = 1000;

    public static bool MaximoValido(int maximo) => maximo is >= 1 and <= MaximoPermitido;

    /// <returns>Quantas mensagens foram reenviadas.</returns>
    public Task<int> ExecutarAsync(int? maximo, CancellationToken cancelamento)
    {
        var quantidade = maximo ?? MaximoPadrao;
        if (!MaximoValido(quantidade))
        {
            throw new ArgumentOutOfRangeException(nameof(maximo), maximo, $"O máximo deve estar entre 1 e {MaximoPermitido}.");
        }

        return filaMensagensMortas.ReenviarAsync(quantidade, cancelamento);
    }
}
