using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using KromaApi.Features.Auth;
using KromaApi.Features.Boards;
using KromaApi.Features.Cards;
using KromaApi.Features.Strokes;
using KromaApi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Base de datos
// ---------------------------------------------------------------------------

var connectionString = builder.Configuration.GetConnectionString("Postgres");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Falta la cadena de conexión 'Postgres'. En local se configura con:\n" +
        "  dotnet user-secrets set \"ConnectionStrings:Postgres\" \"<cadena de Neon>\"\n" +
        "y en producción como variable de entorno " +
        "ConnectionStrings__Postgres.");
}

builder.Services.AddDbContext<KromaDbContext>(options =>
{
    options.UseNpgsql(connectionString)
        // Traduce los nombres de C# al estilo de Postgres: `CreatedAt` pasa a
        // ser `created_at`. Sin esto, PascalCase obliga a entrecomillar cada
        // identificador al escribir SQL a mano en la consola de Neon.
        .UseSnakeCaseNamingConvention();

    if (builder.Environment.IsDevelopment())
    {
        // Muestra los valores de los parámetros en el log de consultas. Es
        // impagable para depurar y una fuga de datos en producción, así que
        // queda limitado a desarrollo.
        options.EnableSensitiveDataLogging();
    }
});

// ---------------------------------------------------------------------------
// Autenticación
// ---------------------------------------------------------------------------

var jwtOptions = new JwtOptions();
builder.Configuration.GetSection(JwtOptions.SectionName).Bind(jwtOptions);

if (string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Falta 'Jwt:Key'. Configúrala como variable de entorno Jwt__Key " +
            "con al menos 32 caracteres aleatorios.");
    }

    // En desarrollo se genera una clave al vuelo para poder arrancar sin
    // configurar nada. Cambia en cada reinicio, así que los tokens emitidos
    // antes dejan de valer: es justo lo que se quiere en local y exactamente
    // lo que no se quiere en producción, y por eso allí se exige configurarla.
    jwtOptions.Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    Console.WriteLine(
        "[aviso] Jwt:Key no configurada; se usa una clave temporal de desarrollo.");
}

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<TokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Sin esto, ASP.NET Core renombra los claims estándar a las URLs largas
        // de Microsoft y `sub` deja de llamarse `sub`.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.Key)),

            // Por defecto se toleran cinco minutos de desfase de reloj entre
            // servidores. Aquí solo hay uno, así que se exige la hora real y un
            // token caducado deja de valer en el acto.
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// CORS
// ---------------------------------------------------------------------------

// El navegador bloquea las llamadas de la app web a otro dominio salvo que la
// API lo autorice. Solo afecta a la versión web: la app de Android no pasa por
// esta comprobación.
const string CorsPolicy = "KromaCors";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // `flutter run -d chrome` abre un puerto distinto en cada
            // ejecución, así que fijar una lista en local sería inútil.
            policy.SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Arranque
// ---------------------------------------------------------------------------

// Aplica las migraciones pendientes al arrancar. Con una sola instancia, como
// en el plan gratuito de Render, es la forma más simple de desplegar: basta con
// subir el código. Con varias instancias habría que sacarlo a un paso aparte,
// porque arrancarían a la vez y competirían por migrar.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KromaDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapBoardEndpoints();
app.MapCardEndpoints();
app.MapStrokeEndpoints();

// Render consulta esta ruta para saber si el servicio sigue vivo. Va sin
// autenticación a propósito: tiene que responder sin credenciales.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithTags("System");

app.Run();

/// <summary>
/// Expuesta para que los tests de integración puedan arrancar la aplicación.
/// </summary>
public partial class Program;
