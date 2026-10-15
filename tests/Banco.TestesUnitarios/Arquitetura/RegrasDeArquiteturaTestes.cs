using System.Reflection;
using NetArchTest.Rules;
using MarcadorAplicacao = Banco.Aplicacao.Identidade.MarcadorIdentidade;
using MarcadorDominio = Banco.Dominio.Compartilhado.MarcadorCompartilhado;
using MarcadorInfraestrutura = Banco.Infraestrutura.Identidade.MarcadorIdentidade;

namespace Banco.TestesUnitarios.Arquitetura;

// Regra de dependência da Clean Architecture e isolamento entre contextos (design D2).
public class RegrasDeArquiteturaTestes
{
    private static readonly Assembly _dominio = typeof(MarcadorDominio).Assembly;
    private static readonly Assembly _aplicacao = typeof(MarcadorAplicacao).Assembly;
    private static readonly Assembly _infraestrutura = typeof(MarcadorInfraestrutura).Assembly;

    private static readonly string[] _camadas = ["Dominio", "Aplicacao", "Infraestrutura"];
    private static readonly string[] _contextos = ["Identidade", "Lancamentos", "Consolidado"];

    public static TheoryData<string, string> CamadasEContextos()
    {
        var dados = new TheoryData<string, string>();
        foreach (var camada in _camadas)
        {
            foreach (var contexto in _contextos)
            {
                dados.Add(camada, contexto);
            }
        }

        return dados;
    }

    [Fact]
    public void Dominio_nao_deve_depender_de_pacote_nem_de_outro_projeto()
    {
        Assert.Empty(LeitorDependencias.DependenciasDeclaradas(_dominio));
        Assert.Empty(LeitorDependencias.ReferenciasCompiladasForaDaPlataforma(_dominio));
    }

    [Fact]
    public void Aplicacao_deve_depender_somente_do_Dominio()
    {
        // Lista fechada: qualquer outra coisa (Infraestrutura, EF Core, ASP.NET, Npgsql, RabbitMQ, Redis...) falha.
        Assert.Equal(["Banco.Dominio"], LeitorDependencias.DependenciasDeclaradas(_aplicacao));
        Assert.Empty(LeitorDependencias.ReferenciasCompiladasForaDaPlataforma(_aplicacao).Except(["Banco.Dominio"]));
    }

    [Theory]
    [MemberData(nameof(CamadasEContextos))]
    public void Contexto_nao_deve_usar_tipos_de_outro_contexto_apenas_do_Compartilhado(string camada, string contexto)
    {
        var namespaceDoContexto = $"Banco.{camada}.{contexto}";
        var tipos = Types.InAssembly(AssemblyDaCamada(camada)).That().ResideInNamespace(namespaceDoContexto);

        // Garante que a regra opera sobre algo (e pega erro de digitação no namespace).
        Assert.NotEmpty(tipos.GetTypes());

        // NetArchTest inspeciona o IL (Mono.Cecil): assinaturas, campos e corpos de métodos.
        var outrosContextos = _contextos
            .Where(c => c != contexto)
            .SelectMany(c => _camadas.Select(l => $"Banco.{l}.{c}"))
            .ToArray();
        var resultado = tipos.ShouldNot().HaveDependencyOnAny(outrosContextos).GetResult();

        Assert.True(
            resultado.IsSuccessful,
            $"Tipos de {namespaceDoContexto} usando outro contexto: {string.Join(", ", resultado.FailingTypeNames ?? [])}");
    }

    private static Assembly AssemblyDaCamada(string camada) => camada switch
    {
        "Dominio" => _dominio,
        "Aplicacao" => _aplicacao,
        "Infraestrutura" => _infraestrutura,
        _ => throw new ArgumentOutOfRangeException(nameof(camada), camada, null),
    };
}
