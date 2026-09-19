using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using KromaApi.Domain;

namespace KromaApi.Features.Auth;

/// <summary>
/// Opciones del token, leídas de la configuración.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Clave secreta con la que se firman los tokens.
    /// </summary>
    /// <remarks>
    /// Quien tenga esta clave puede fabricar tokens válidos para cualquier
    /// cuenta, así que nunca debe estar en <c>appsettings.json</c>: ese archivo
    /// va a Git. En local se guarda con <c>dotnet user-secrets</c> y en
    /// producción como variable de entorno del servidor.
    /// </remarks>
    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = "KromaApi";
    public string Audience { get; set; } = "Kroma";

    /// <summary>
    /// Días que vale un token antes de caducar.
    /// </summary>
    /// <remarks>
    /// Treinta días es largo para una API pública, pero aquí es lo razonable:
    /// lo correcto sería emitir tokens de una hora y renovarlos con un
    /// <i>refresh token</i>, y eso es bastante maquinaria para dos usuarias.
    /// La alternativa —tokens cortos sin renovación— obligaría a tu hermana a
    /// iniciar sesión cada hora, que es peor.
    /// </remarks>
    public int ExpirationDays { get; set; } = 30;
}

/// <summary>
/// Emite los tokens JWT con los que la app se identifica en cada petición.
/// </summary>
public class TokenService(JwtOptions options)
{
    public string CreateToken(User user)
    {
        var securityKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(options.Key));

        var descriptor = new SecurityTokenDescriptor
        {
            // Los «claims» son los datos que el token lleva dentro y que el
            // servidor puede leer sin consultar la base de datos. Van firmados,
            // no cifrados: cualquiera puede leerlos, pero nadie puede alterarlos
            // sin invalidar la firma. Por eso aquí no va nada secreto.
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),

                // Identificador único del token. Permitirá revocarlo en el
                // futuro sin tener que cambiar la clave de firma, que echaría
                // a todo el mundo a la vez.
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            ]),
            Expires = DateTime.UtcNow.AddDays(options.ExpirationDays),
            Issuer = options.Issuer,
            Audience = options.Audience,
            SigningCredentials = new SigningCredentials(
                securityKey,
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
