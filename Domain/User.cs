namespace KromaApi.Domain;

/// <summary>
/// Una persona con cuenta en la aplicación.
/// </summary>
public class User
{
    /// <summary>
    /// Identificador en formato UUID versión 7.
    /// </summary>
    /// <remarks>
    /// El v7 lleva la marca de tiempo en sus bits altos, así que ordena
    /// cronológicamente. Eso importa en Postgres: las claves v4, al ser
    /// aleatorias, dispersan las inserciones por todo el índice B-tree y lo
    /// fragmentan, mientras que las v7 entran siempre al final. Es el mismo
    /// criterio que sigue la app de Flutter al crear los trazos.
    /// </remarks>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Correo normalizado a minúsculas, único en toda la base de datos.
    /// </summary>
    /// <remarks>
    /// Se normaliza al registrarse en vez de comparar sin distinguir
    /// mayúsculas al consultar. Así el índice único hace el trabajo y no puede
    /// colarse un duplicado del tipo Ana@… frente a ana@….
    /// </remarks>
    public required string Email { get; set; }

    /// <summary>
    /// Hash de la contraseña. Nunca la contraseña en claro.
    /// </summary>
    public required string PasswordHash { get; set; }

    /// <summary>
    /// Nombre visible, para saludar y para mostrar quién creó cada cosa.
    /// </summary>
    public required string DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Board> Boards { get; set; } = [];
}
