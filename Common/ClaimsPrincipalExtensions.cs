using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KromaApi.Common;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Saca del token el identificador de quien hace la petición.
    /// </summary>
    /// <remarks>
    /// Busca en dos sitios porque ASP.NET Core, salvo que se le diga lo
    /// contrario, traduce el claim estándar <c>sub</c> al nombre largo de
    /// Microsoft <c>NameIdentifier</c>. Este proyecto desactiva esa traducción,
    /// pero comprobar ambos evita que un cambio de configuración deje la
    /// autenticación rota de una forma difícil de diagnosticar.
    ///
    /// Solo debe llamarse desde endpoints protegidos: si no hay token válido,
    /// no hay identificador que sacar.
    /// </remarks>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(value, out var id)) return id;

        throw new InvalidOperationException(
            "El token no contiene un identificador de usuario válido.");
    }
}
