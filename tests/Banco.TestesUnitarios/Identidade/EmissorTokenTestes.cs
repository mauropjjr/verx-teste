using System.Security.Cryptography;
using Banco.Dominio.Identidade;
using Banco.Infraestrutura.Identidade.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Banco.TestesUnitarios.Identidade;

// Emissão do JWT (design D10) e gerenciamento do par de chaves RSA que o assina.
public sealed class EmissorTokenTestes : IDisposable
{
    private static readonly DateTimeOffset _agora = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "banco-chaves-" + Guid.NewGuid().ToString("N"));

    private string CaminhoChave => Path.Combine(_pasta, "identidade-rsa.pem");

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
        {
            Directory.Delete(_pasta, recursive: true);
        }
    }

    [Fact]
    public void Token_emitido_e_RS256_com_kid_emissor_audiencia_sub_name_papeis_e_expira_em_30_minutos()
    {
        using var chaves = new ProvedorChavesRsa(new OpcoesChavesIdentidade { CaminhoArquivo = CaminhoChave });
        var emissor = new EmissorTokenJwt(chaves, new RelogioFixo(_agora));
        var usuario = Usuario.Criar(Guid.NewGuid(), "operador", "Operador Demo", "hash", ["operador", "consulta"]);

        var emitido = emissor.Emitir(usuario);

        var token = new JsonWebToken(emitido.TokenAcesso);
        Assert.Equal("RS256", token.Alg);
        Assert.False(string.IsNullOrEmpty(token.Kid));
        Assert.Equal(chaves.Chave.KeyId, token.Kid);
        Assert.Equal("banco-identidade", token.Issuer);
        Assert.Equal(["banco-api"], token.Audiences);
        Assert.Equal(usuario.Id.ToString(), token.Subject);
        Assert.Equal("Operador Demo", token.GetClaim("name").Value);
        Assert.Equal(["operador", "consulta"], token.Claims.Where(c => c.Type == "role").Select(c => c.Value));
        Assert.Equal(_agora.AddMinutes(30).UtcDateTime, token.ValidTo);
        Assert.Equal(_agora.AddMinutes(30), emitido.ExpiraEm);
    }

    [Fact]
    public void Par_de_chaves_e_gerado_na_primeira_vez_e_reaproveitado_do_arquivo_depois()
    {
        using var primeira = new ProvedorChavesRsa(new OpcoesChavesIdentidade { CaminhoArquivo = CaminhoChave });

        Assert.True(File.Exists(CaminhoChave));

        using var segunda = new ProvedorChavesRsa(new OpcoesChavesIdentidade { CaminhoArquivo = CaminhoChave });
        Assert.Equal(primeira.Chave.KeyId, segunda.Chave.KeyId);
        Assert.Equal(primeira.Chave.Rsa.ExportParameters(false).Modulus, segunda.Chave.Rsa.ExportParameters(false).Modulus);
    }

    [Fact]
    public void Chave_privada_informada_por_variavel_tem_precedencia_e_nao_grava_arquivo()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();

        using var chaves = new ProvedorChavesRsa(new OpcoesChavesIdentidade { ChavePrivadaPem = pem, CaminhoArquivo = CaminhoChave });

        Assert.Equal(rsa.ExportParameters(false).Modulus, chaves.Chave.Rsa.ExportParameters(false).Modulus);
        Assert.False(File.Exists(CaminhoChave));
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
