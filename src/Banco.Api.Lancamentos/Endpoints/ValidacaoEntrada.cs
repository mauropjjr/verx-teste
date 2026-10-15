using System.Globalization;
using Banco.Aplicacao.Lancamentos;
using Banco.Dominio.Lancamentos;
using Banco.Infraestrutura.Lancamentos.Persistencia;
using FluentValidation;
using FluentValidation.Results;

namespace Banco.Api.Lancamentos.Endpoints;

/// <summary>
/// Validação de formato e tamanho de rota, cabeçalho e corpo (spec access-control "Validação de entrada",
/// design D11) antes de chamar o caso de uso. As chaves dos erros são os nomes que o cliente enviou
/// (<c>contaId</c>, <c>Idempotency-Key</c>, <c>tipo</c>, <c>valor</c>, <c>descricao</c>...).
/// </summary>
internal static class ValidacaoEntrada
{
    public const string CabecalhoIdempotencia = "Idempotency-Key";

    public static IServiceCollection AdicionarValidacaoLancamentos(this IServiceCollection servicos) =>
        servicos
            .AddSingleton<IValidator<EntradaRegistro>, ValidadorEntradaRegistro>()
            .AddSingleton<IValidator<EntradaEstorno>, ValidadorEntradaEstorno>()
            .AddSingleton<IValidator<EntradaExtrato>, ValidadorEntradaExtrato>();

    /// <summary>Datas da query em <c>yyyy-MM-dd</c>.</summary>
    public static bool TentarLerData(string? texto, out DateOnly data) =>
        DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out data);

    public static IRuleBuilderOptions<T, string> ContaIdValido<T>(this IRuleBuilder<T, string> regra) =>
        regra.Matches("^[A-Za-z0-9-]{1,20}$")
            .WithMessage($"Identificador de conta inválido (letras, dígitos e hífen, até {Conta.TamanhoMaximoId} caracteres).")
            .OverridePropertyName("contaId");

    public static IRuleBuilderOptions<T, string?> ChaveIdempotenciaValida<T>(this IRuleBuilder<T, string?> regra) =>
        regra.NotEmpty()
            .WithMessage($"O cabeçalho {CabecalhoIdempotencia} é obrigatório.")
            .Matches("^[\\x21-\\x7E]+$")
            .WithMessage($"O cabeçalho {CabecalhoIdempotencia} aceita apenas caracteres ASCII visíveis.")
            .MaximumLength(Lancamento.TamanhoMaximoChaveIdempotencia)
            .WithMessage($"O cabeçalho {CabecalhoIdempotencia} tem no máximo {Lancamento.TamanhoMaximoChaveIdempotencia} caracteres.")
            .OverridePropertyName(CabecalhoIdempotencia);

    public static IResult ProblemaValidacao(this ValidationResult resultado) =>
        TypedResults.ValidationProblem(resultado.ToDictionary());
}

/// <summary>Tudo o que chega no <c>POST .../lancamentos</c>.</summary>
internal sealed record EntradaRegistro(string ContaId, string? ChaveIdempotencia, RequisicaoLancamento? Corpo);

/// <summary>Corpo do registro: <c>tipo</c> (<c>CREDITO</c>/<c>DEBITO</c>), <c>valor</c> e <c>descricao</c>.</summary>
internal sealed record RequisicaoLancamento(string? Tipo, decimal? Valor, string? Descricao);

internal sealed class ValidadorEntradaRegistro : AbstractValidator<EntradaRegistro>
{
    public ValidadorEntradaRegistro()
    {
        RuleFor(e => e.ContaId).ContaIdValido();
        RuleFor(e => e.ChaveIdempotencia).ChaveIdempotenciaValida();
        RuleFor(e => e.Corpo).NotNull().WithMessage("Informe o corpo do lançamento.").OverridePropertyName("corpo");

        When(e => e.Corpo is not null, () =>
        {
            RuleFor(e => e.Corpo!.Tipo)
                .Must(t => t is ConversoesLancamentos.Credito or ConversoesLancamentos.Debito)
                .WithMessage($"Informe o tipo: {ConversoesLancamentos.Credito} ou {ConversoesLancamentos.Debito}.")
                .OverridePropertyName("tipo");
            RuleFor(e => e.Corpo!.Valor)
                .Must(v => v is { } valor && Lancamento.ValorValido(valor))
                .WithMessage($"O valor deve ser maior que zero, até {Lancamento.ValorMaximo} e ter no máximo {Lancamento.CasasDecimais} casas decimais.")
                .OverridePropertyName("valor");
            RuleFor(e => e.Corpo!.Descricao)
                .Must(d => !string.IsNullOrWhiteSpace(d) && d.Trim().Length <= Lancamento.TamanhoMaximoDescricao)
                .WithMessage($"Informe a descrição (até {Lancamento.TamanhoMaximoDescricao} caracteres).")
                .OverridePropertyName("descricao");
        });
    }
}

/// <summary>Tudo o que chega no <c>POST .../lancamentos/{id}/estorno</c> além do id (já tipado pela rota).</summary>
internal sealed record EntradaEstorno(string ContaId, string? ChaveIdempotencia);

internal sealed class ValidadorEntradaEstorno : AbstractValidator<EntradaEstorno>
{
    public ValidadorEntradaEstorno()
    {
        RuleFor(e => e.ContaId).ContaIdValido();
        RuleFor(e => e.ChaveIdempotencia).ChaveIdempotenciaValida();
    }
}

/// <summary>Rota e query do <c>GET .../lancamentos?de&amp;ate&amp;limite&amp;cursor</c>.</summary>
internal sealed record EntradaExtrato(string ContaId, string? De, string? Ate, int? Limite, string? Cursor);

internal sealed class ValidadorEntradaExtrato : AbstractValidator<EntradaExtrato>
{
    public ValidadorEntradaExtrato()
    {
        RuleFor(e => e.ContaId).ContaIdValido();
        RuleFor(e => e.De)
            .Must(d => d is null || ValidacaoEntrada.TentarLerData(d, out _))
            .WithMessage("Data inicial inválida (use yyyy-MM-dd).")
            .OverridePropertyName("de");
        RuleFor(e => e.Ate)
            .Must(a => a is null || ValidacaoEntrada.TentarLerData(a, out _))
            .WithMessage("Data final inválida (use yyyy-MM-dd).")
            .OverridePropertyName("ate");
        RuleFor(e => e)
            .Must(e => !ValidacaoEntrada.TentarLerData(e.De, out var de) || !ValidacaoEntrada.TentarLerData(e.Ate, out var ate) || de <= ate)
            .WithMessage("A data inicial deve ser anterior ou igual à final.")
            .OverridePropertyName("ate");
        RuleFor(e => e.Limite)
            .InclusiveBetween(1, ConsultarExtrato.LimiteMaximo)
            .WithMessage($"O limite deve estar entre 1 e {ConsultarExtrato.LimiteMaximo}.")
            .OverridePropertyName("limite");
        RuleFor(e => e.Cursor)
            .Must(c => c is null || CursorExtrato.TentarDecodificar(c, out _))
            .WithMessage("Cursor inválido.")
            .OverridePropertyName("cursor");
    }
}
