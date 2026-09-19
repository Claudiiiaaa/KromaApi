namespace KromaApi.Domain;

/// <summary>
/// Una tarjeta del tablero. Tiene título escrito y, dentro, un lienzo para
/// escribir a mano.
/// </summary>
public class Card
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ColumnId { get; set; }
    public BoardColumn? Column { get; set; }

    /// <summary>
    /// Título en texto, para leer el tablero de un vistazo y poder buscar.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>Orden dentro de su columna. Ver <see cref="BoardColumn.Position"/>.</summary>
    public double Position { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Última modificación, incluida la tinta de su lienzo.
    /// </summary>
    /// <remarks>
    /// Es la pieza sobre la que se construirá la sincronización: el cliente
    /// pedirá «lo que haya cambiado desde tal instante» en vez de descargar el
    /// tablero entero cada vez.
    /// </remarks>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Stroke> Strokes { get; set; } = [];
}
