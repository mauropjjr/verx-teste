namespace Banco.Dominio.Lancamentos;

/// <summary>
/// Conta que recebe lançamentos. Contas e titulares vêm do seed (abertura de contas é non-goal); o nome completo
/// do titular nunca sai do serviço: respostas usam <see cref="TitularMascarado"/> (design D11).
/// </summary>
public sealed class Conta
{
    public const int TamanhoMaximoId = 20;
    public const int TamanhoMaximoTitular = 200;

    private Conta(string id, string titular, SituacaoConta situacao)
    {
        Id = id;
        Titular = titular;
        Situacao = situacao;
    }

    /// <summary>Identificador legível (ex.: <c>CC-0001</c>).</summary>
    public string Id { get; private set; }

    public string Titular { get; private set; }

    public SituacaoConta Situacao { get; private set; }

    public bool EstaAtiva => Situacao == SituacaoConta.Ativa;

    /// <summary>
    /// Primeiro nome com até 3 letras visíveis (sempre escondendo ao menos uma) e só a inicial dos demais:
    /// <c>Maria Silva</c> ⇒ <c>Mar*** S***</c>.
    /// </summary>
    public string TitularMascarado
    {
        get
        {
            var partes = Titular.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var visiveisPrimeiro = Math.Min(3, Math.Max(1, partes[0].Length - 1));
            return string.Join(' ', partes.Select((parte, i) => parte[..(i == 0 ? visiveisPrimeiro : 1)] + "***"));
        }
    }

    public static Conta Criar(string id, string titular, SituacaoConta situacao = SituacaoConta.Ativa)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(titular);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id.Length, TamanhoMaximoId, nameof(id));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(titular.Trim().Length, TamanhoMaximoTitular, nameof(titular));
        return new Conta(id, titular.Trim(), situacao);
    }
}
