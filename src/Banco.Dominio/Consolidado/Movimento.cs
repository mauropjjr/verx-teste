namespace Banco.Dominio.Consolidado;

/// <summary>Natureza de um movimento visto pelo Consolidado.</summary>
public enum NaturezaMovimento
{
    Credito = 1,
    Debito = 2,
}

/// <summary>
/// Lançamento como o Consolidado o enxerga (projeção do evento <c>LancamentoRegistrado</c>): valor sempre positivo
/// com até 2 casas e a natureza que define o sinal. Pertence ao dia UTC do instante em que ocorreu (spec
/// balance-consolidation "Consolidar lançamentos por dia").
/// </summary>
public sealed record Movimento
{
    public const int TamanhoMaximoContaId = 20;
    public const int CasasDecimais = 2;

    /// <summary>Maior valor que cabe em <c>numeric(18,2)</c>.</summary>
    public const decimal ValorMaximo = 9_999_999_999_999_999.99m;

    private Movimento(Guid id, string contaId, NaturezaMovimento natureza, decimal valor, DateTimeOffset ocorridoEm)
    {
        Id = id;
        ContaId = contaId;
        Natureza = natureza;
        Valor = valor;
        OcorridoEm = ocorridoEm;
    }

    /// <summary>Id do lançamento de origem.</summary>
    public Guid Id { get; }

    public string ContaId { get; }

    public NaturezaMovimento Natureza { get; }

    public decimal Valor { get; }

    /// <summary>Instante em UTC.</summary>
    public DateTimeOffset OcorridoEm { get; }

    public DateOnly Dia => DateOnly.FromDateTime(OcorridoEm.UtcDateTime);

    /// <summary>Efeito no saldo: positivo para crédito, negativo para débito.</summary>
    public decimal Liquido => Natureza == NaturezaMovimento.Credito ? Valor : -Valor;

    public static Movimento Criar(Guid id, string contaId, NaturezaMovimento natureza, decimal valor, DateTimeOffset ocorridoEm)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("O id do lançamento é obrigatório.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(contaId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(contaId.Length, TamanhoMaximoContaId, nameof(contaId));
        if (!Enum.IsDefined(natureza))
        {
            throw new ArgumentOutOfRangeException(nameof(natureza), natureza, "Natureza de movimento desconhecida.");
        }

        if (valor <= 0 || valor > ValorMaximo || decimal.Round(valor, CasasDecimais) != valor)
        {
            throw new ArgumentOutOfRangeException(nameof(valor), valor, $"O valor deve ser positivo, até {ValorMaximo} e ter no máximo {CasasDecimais} casas decimais.");
        }

        if (ocorridoEm == default)
        {
            throw new ArgumentException("O instante de ocorrência é obrigatório.", nameof(ocorridoEm));
        }

        return new Movimento(id, contaId, natureza, valor, ocorridoEm.ToUniversalTime());
    }
}
