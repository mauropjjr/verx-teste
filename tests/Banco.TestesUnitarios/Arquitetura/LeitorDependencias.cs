using System.Reflection;
using System.Text.Json;

namespace Banco.TestesUnitarios.Arquitetura;

/// <summary>
/// Dependências de um assembly do Banco por duas vias complementares: (1) referências compiladas
/// (<see cref="Assembly.GetReferencedAssemblies"/>), que revelam o uso real de tipos; e (2) o fecho
/// transitivo das dependências declaradas (ProjectReference/PackageReference), lido do <c>.deps.json</c>
/// deste projeto de teste, que revela referências mesmo quando o compilador as descarta por não usá-las.
/// </summary>
internal static class LeitorDependencias
{
    /// <summary>Referências compiladas, sem as da própria plataforma .NET (System.*, Microsoft.Win32.*, netstandard).</summary>
    public static IReadOnlyCollection<string> ReferenciasCompiladasForaDaPlataforma(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(nome => nome is not ("System" or "netstandard" or "mscorlib")
                && !nome.StartsWith("System.", StringComparison.Ordinal)
                && !nome.StartsWith("Microsoft.Win32.", StringComparison.Ordinal))
            .Order()
            .ToArray();

    /// <summary>Fecho transitivo das dependências declaradas de um projeto, segundo o deps.json.</summary>
    public static IReadOnlyCollection<string> DependenciasDeclaradas(Assembly assembly)
    {
        var nomeProjeto = assembly.GetName().Name!;
        var alvos = LerAlvosDoDepsJson();
        if (!alvos.TryGetValue(nomeProjeto, out var raiz))
        {
            throw new InvalidOperationException($"Projeto '{nomeProjeto}' não encontrado no deps.json do teste.");
        }

        var visitados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendentes = new Stack<string>(Dependencias(raiz));
        while (pendentes.Count > 0)
        {
            var nome = pendentes.Pop();
            if (visitados.Add(nome) && alvos.TryGetValue(nome, out var alvo))
            {
                foreach (var dependencia in Dependencias(alvo))
                {
                    pendentes.Push(dependencia);
                }
            }
        }

        return visitados.Order().ToArray();
    }

    private static string[] Dependencias(JsonElement alvo) =>
        alvo.TryGetProperty("dependencies", out var dependencias)
            ? dependencias.EnumerateObject().Select(p => p.Name).ToArray()
            : [];

    private static Dictionary<string, JsonElement> LerAlvosDoDepsJson()
    {
        var assemblyDeTeste = Assembly.GetExecutingAssembly().GetName().Name!;
        var caminho = Path.Combine(AppContext.BaseDirectory, $"{assemblyDeTeste}.deps.json");
        using var documento = JsonDocument.Parse(File.ReadAllText(caminho));
        var alvos = documento.RootElement.GetProperty("targets").EnumerateObject().First().Value;
        return alvos.EnumerateObject().ToDictionary(
            p => p.Name.Split('/')[0],
            p => p.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
    }
}
