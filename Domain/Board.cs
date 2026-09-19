namespace KromaApi.Domain;

/// <summary>
/// Un tablero: el contenedor de columnas al estilo de Notion o Trello.
/// </summary>
public class Board
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Quién es el dueño del tablero.
    /// </summary>
    /// <remarks>
    /// Toda consulta de la API filtra por este campo comparándolo con el
    /// usuario del token. Es la frontera de seguridad del sistema: sin ese
    /// filtro, cambiar un identificador en la URL dejaría leer los tableros de
    /// cualquier otra persona.
    /// </remarks>
    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    public required string Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<BoardColumn> Columns { get; set; } = [];
}
