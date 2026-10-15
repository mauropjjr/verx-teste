namespace Banco.Dominio.Lancamentos;

/// <summary>
/// Movimentação financeira imutável de uma conta (design D3): valor positivo com até 2 casas, tipo
/// crédito/débito e instante atribuído pelo servidor. Correção só por <see cref="Estornar"/> — lançamento
/// compensatório de tipo oposto e mesmo valor, que por sua vez não pode ser estornado.
/// A chave de idempotência e o hash da requisição que o originou viajam junto (design D4).
/// </summary>
public sealed class Lancamento
{
    public const int TamanhoMaximoDescricao = 140;
    public const int CasasDecimais = 2;
    public const int TamanhoMaximoChaveIdempotencia = 100;

    /// <summary>Maior valor que cabe em <c>numeric(18,2)</c>.</summary>
    public const decimal ValorMaximo = 9_999_999_999_999_999.99m;

    private Lancamento(
        Guid id,
        string contaId,
        TipoLancamento tipo,
        decimal valor,
        string descricao,
        DateTimeOffset ocorridoEm,
        Guid? estornoDe,
        string chaveIdempotencia,
        string hashRequisicao)
    {
        Id = id;
        ContaId = contaId;
        Tipo = tipo;
        Valor = valor;
        Descricao = descricao;
        OcorridoEm = ocorridoEm;
        EstornoDe = estornoDe;
        ChaveIdempotencia = chaveIdempotencia;
        HashRequisicao = hashRequisicao;
    }

    /// <summary>UUID v7 (ordenável pelo tempo).</summary>
    public Guid Id { get; private set; }

    public string ContaId { get; private set; }

    public TipoLancamento Tipo { get; private set; }

    public decimal Valor { get; private set; }

    public string Descricao { get; private set; }

    /// <summary>Instante UTC, com precisão de microssegundos (a mesma do <c>timestamptz</c>).</summary>
    public DateTimeOffset OcorridoEm { get; private set; }

    /// <summary>Lançamento original, quando este é um estorno.</summary>
    public Guid? EstornoDe { get; private set; }

    public string ChaveIdempotencia { get; private set; }

    public string HashRequisicao { get; private set; }

    public bool EhEstorno => EstornoDe is not null;

    public static Lancamento Registrar(
        string contaId,
        TipoLancamento tipo,
        decimal valor,
        string descricao,
        DateTimeOffset ocorridoEm,
        string chaveIdempotencia,
        string hashRequisicao)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descricao);
        ValidarIdempotencia(chaveIdempotencia, hashRequisicao);
        if (!Enum.IsDefined(tipo))
        {
            throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de lançamento desconhecido.");
        }

        if (!ValorValido(valor))
        {
            throw new ArgumentOutOfRangeException(nameof(valor), valor, $"O valor deve ser positivo, até {ValorMaximo} e ter no máximo {CasasDecimais} casas decimais.");
        }

        var descricaoNormalizada = descricao.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(descricaoNormalizada.Length, TamanhoMaximoDescricao, nameof(descricao));

        var instante = NormalizarInstante(ocorridoEm);
        return new Lancamento(
            Guid.CreateVersion7(instante),
            contaId,
            tipo,
            valor,
            descricaoNormalizada,
            instante,
            estornoDe: null,
            chaveIdempotencia,
            hashRequisicao);
    }

    /// <summary>Valor positivo, até <see cref="ValorMaximo"/> e sem mais de 2 casas significativas.</summary>
    public static bool ValorValido(decimal valor) =>
        valor > 0 && valor <= ValorMaximo && decimal.Round(valor, CasasDecimais) == valor;

    /// <summary>Cria o lançamento compensatório: tipo oposto, mesmo valor, <see cref="EstornoDe"/> = este.</summary>
    public Lancamento Estornar(DateTimeOffset ocorridoEm, string chaveIdempotencia, string hashRequisicao)
    {
        if (EhEstorno)
        {
            throw new InvalidOperationException("Um lançamento de estorno não pode ser estornado.");
        }

        ValidarIdempotencia(chaveIdempotencia, hashRequisicao);

        var instante = NormalizarInstante(ocorridoEm);
        return new Lancamento(
            Guid.CreateVersion7(instante),
            ContaId,
            Tipo == TipoLancamento.Credito ? TipoLancamento.Debito : TipoLancamento.Credito,
            Valor,
            $"Estorno do lançamento {Id}",
            instante,
            Id,
            chaveIdempotencia,
            hashRequisicao);
    }

    private static void ValidarIdempotencia(string chaveIdempotencia, string hashRequisicao)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chaveIdempotencia);
        ArgumentException.ThrowIfNullOrWhiteSpace(hashRequisicao);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chaveIdempotencia.Length, TamanhoMaximoChaveIdempotencia, nameof(chaveIdempotencia));
    }

    // UTC e truncado em microssegundos: o 201 devolve exatamente o que o extrato vai ler do banco.
    private static DateTimeOffset NormalizarInstante(DateTimeOffset instante)
    {
        var utc = instante.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
