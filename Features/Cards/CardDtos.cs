namespace KromaApi.Features.Cards;

public record CreateCardRequest(string Title, double? Position);

public record UpdateCardRequest(string Title);

/// <summary>
/// Mueve una tarjeta a una columna y una posición concretas.
/// </summary>
/// <remarks>
/// La posición la calcula el cliente, no el servidor, y es lo que hace que
/// arrastrar sea instantáneo: la app ya conoce el orden de la columna, así que
/// saca la media entre las dos tarjetas vecinas y la envía. El servidor solo
/// comprueba permisos y guarda. Si en su lugar mandáramos un índice, el
/// servidor tendría que releer la columna entera y renumerar.
/// </remarks>
public record MoveCardRequest(Guid TargetColumnId, double Position);
