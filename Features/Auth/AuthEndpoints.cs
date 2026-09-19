using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using KromaApi.Common;
using KromaApi.Domain;
using KromaApi.Infrastructure;

namespace KromaApi.Features.Auth;

public static class AuthEndpoints
{
    /// <summary>Longitud mínima de contraseña.</summary>
    /// <remarks>
    /// Se exige longitud y no «una mayúscula, un número y un símbolo». Las
    /// reglas de composición empujan a contraseñas del tipo «Password1!», que
    /// son cortas y predecibles; la longitud es lo que de verdad cuesta romper,
    /// y es también lo que recomienda hoy el NIST.
    /// </remarks>
    private const int MinPasswordLength = 8;

    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", Register);
        group.MapPost("/login", Login);
        group.MapGet("/me", Me).RequireAuthorization();
        group.MapDelete("/me", DeleteMe).RequireAuthorization();

        return group;
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        KromaDbContext db,
        TokenService tokens,
        JwtOptions jwtOptions,
        CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;

        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            errors["email"] = ["Introduce un correo válido."];

        if (string.IsNullOrEmpty(request.Password) ||
            request.Password.Length < MinPasswordLength)
            errors["password"] = [$"La contraseña debe tener al menos {MinPasswordLength} caracteres."];

        if (string.IsNullOrWhiteSpace(displayName))
            errors["displayName"] = ["Escribe tu nombre."];

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Ya existe una cuenta con ese correo."],
            });
        }

        var user = new User
        {
            Email = email,
            DisplayName = displayName,
            // El hash se calcula con PBKDF2 y una sal distinta por usuario, que
            // es lo que impide que una sola tabla precalculada sirva para
            // romper todas las contraseñas de la base de datos a la vez.
            PasswordHash = string.Empty,
        };
        user.PasswordHash = new PasswordHasher<User>()
            .HashPassword(user, request.Password);

        db.Users.Add(user);

        // Se le crea su primer tablero ya montado. Entrar en una app vacía y
        // tener que averiguar por dónde empezar es la forma más rápida de que
        // alguien la cierre y no vuelva.
        db.Boards.Add(CreateDefaultBoard(user.Id));

        await db.SaveChangesAsync(ct);

        return Results.Ok(BuildResponse(user, tokens, jwtOptions));
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        KromaDbContext db,
        TokenService tokens,
        JwtOptions jwtOptions,
        CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // El mismo mensaje tanto si el correo no existe como si la contraseña
        // falla. Distinguirlos permitiría averiguar qué correos están dados de
        // alta probándolos uno a uno.
        if (user is null) return InvalidCredentials();

        var hasher = new PasswordHasher<User>();
        var result = hasher.VerifyHashedPassword(
            user, user.PasswordHash, request.Password);

        if (result == PasswordVerificationResult.Failed) return InvalidCredentials();

        // El algoritmo de hash se endurece con cada versión de .NET. Cuando eso
        // pasa, la verificación avisa y se vuelve a calcular aprovechando que
        // aquí, y solo aquí, se tiene la contraseña en claro.
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(BuildResponse(user, tokens, jwtOptions));
    }

    private static async Task<IResult> Me(
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var user = await db.Users.FindAsync([userId], ct);

        // El token es válido pero la cuenta ya no está: pasa si se borró la
        // cuenta mientras el token seguía vigente.
        if (user is null) return Results.NotFound();

        return Results.Ok(new UserDto(user.Id, user.Email, user.DisplayName));
    }

    /// <summary>
    /// Borra la cuenta de quien la pide, con todo su contenido.
    /// </summary>
    /// <remarks>
    /// Un solo DELETE sobre el usuario: las claves ajenas en cascada se llevan
    /// por delante sus tableros, columnas, tarjetas y trazos. No hace falta
    /// recorrer nada a mano, y al ser una única sentencia no puede quedarse a
    /// medias dejando tarjetas huérfanas.
    ///
    /// Solo puede borrar su propia cuenta: el identificador sale del token, no
    /// de la URL, así que no hay forma de pedir el borrado de otra persona.
    /// </remarks>
    private static async Task<IResult> DeleteMe(
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        var deleted = await db.Users
            .Where(u => u.Id == userId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static IResult InvalidCredentials() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["credentials"] = ["El correo o la contraseña no son correctos."],
        });

    private static AuthResponse BuildResponse(
        User user, TokenService tokens, JwtOptions options) =>
        new(
            tokens.CreateToken(user),
            DateTimeOffset.UtcNow.AddDays(options.ExpirationDays),
            new UserDto(user.Id, user.Email, user.DisplayName));

    /// <summary>Tablero inicial con las tres columnas clásicas.</summary>
    private static Board CreateDefaultBoard(Guid ownerId) => new()
    {
        OwnerId = ownerId,
        Title = "Mi tablero",
        Columns =
        [
            new BoardColumn { Title = "Por hacer", Position = 1000 },
            new BoardColumn { Title = "Haciendo", Position = 2000 },
            new BoardColumn { Title = "Hecho", Position = 3000 },
        ],
    };
}
