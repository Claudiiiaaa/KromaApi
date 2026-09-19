using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace KromaApi.Infrastructure;

/// <summary>
/// Construye el contexto para las herramientas de línea de comandos de EF Core.
/// </summary>
/// <remarks>
/// Cuando se ejecuta <c>dotnet ef</c>, EF necesita una instancia del contexto.
/// Si existe una clase como esta, la usa y <b>no</b> arranca la aplicación, así
/// que es aquí donde hay que resolver la cadena de conexión.
///
/// Se busca en el mismo sitio que en producción —secretos de usuario y
/// variables de entorno— y solo si no hay nada se recurre a una cadena falsa.
/// Ese último recurso permite ejecutar <c>migrations add</c> en una máquina sin
/// configurar, porque generar una migración compara el modelo de C# con las
/// migraciones anteriores y no necesita conectarse a ninguna base de datos.
/// Aplicarla, en cambio, sí, y entonces la cadena falsa daría el error
/// «Failed to connect to 127.0.0.1:5432».
/// </remarks>
public class KromaDbContextFactory : IDesignTimeDbContextFactory<KromaDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Database=notes_design_time;Username=postgres";

    public KromaDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine(
                "[aviso] No hay cadena de conexión configurada; se usa una " +
                "ficticia. Sirve para generar migraciones, no para aplicarlas.");
            connectionString = FallbackConnectionString;
        }

        var options = new DbContextOptionsBuilder<KromaDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new KromaDbContext(options);
    }
}
