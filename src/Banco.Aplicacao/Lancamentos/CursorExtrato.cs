using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Banco.Aplicacao.Lancamentos;

/// <summary>
/// Posição no extrato: o último <c>(ocorridoEm, id)</c> entregue. Viaja ao cliente como texto opaco
/// (base64url), para que ele não dependa do formato.
/// </summary>
public sealed record CursorExtrato(DateTimeOffset OcorridoEm, Guid Id)
{
    public string Codificar() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{OcorridoEm.UtcTicks}|{Id:N}")));

    public static bool TentarDecodificar(string? texto, out CursorExtrato? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(texto) || texto.Length > 100 || !Base64Url.IsValid(texto))
        {
            return false;
        }

        var partes = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(texto)).Split('|');
        if (partes.Length != 2
            || !long.TryParse(partes[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(partes[1], "N", out var id))
        {
            return false;
        }

        cursor = new CursorExtrato(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
