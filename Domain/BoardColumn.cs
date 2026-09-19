namespace KromaApi.Domain;

/// <summary>
/// Una columna del tablero: «Por hacer», «Haciendo», «Hecho»…
/// </summary>
/// <remarks>
/// Se llama <c>BoardColumn</c> y no <c>Column</c> porque «column» es palabra
/// reservada en SQL y acabaría obligando a entrecomillarla en cada consulta.
/// </remarks>
public class BoardColumn
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BoardId { get; set; }
    public Board? Board { get; set; }

    public required string Title { get; set; }

    /// <summary>
    /// Orden de la columna dentro del tablero.
    /// </summary>
    /// <remarks>
    /// Es <c>double</c> y no <c>int</c> a propósito. Con enteros, arrastrar una
    /// columna entre otras dos obligaría a renumerar todas las siguientes y a
    /// enviar una actualización por cada una. Con decimales basta con darle la
    /// media de sus dos vecinas —entre 1 y 2 va 1,5— y solo cambia una fila.
    ///
    /// Su límite: tras muchísimas reordenaciones en el mismo hueco, los
    /// decimales se agotan. Hacen falta más de cincuenta inserciones seguidas
    /// en el mismo punto para llegar ahí, y se resuelve renumerando de vez en
    /// cuando.
    /// </remarks>
    public double Position { get; set; }

    public List<Card> Cards { get; set; } = [];
}
