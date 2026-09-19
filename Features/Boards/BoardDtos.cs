namespace KromaApi.Features.Boards;

public record BoardSummaryDto(
    Guid Id,
    string Title,
    DateTimeOffset UpdatedAt,
    int CardCount);

public record BoardDetailDto(
    Guid Id,
    string Title,
    DateTimeOffset UpdatedAt,
    List<ColumnDto> Columns);

public record ColumnDto(
    Guid Id,
    string Title,
    double Position,
    List<CardDto> Cards);

public record CardDto(
    Guid Id,
    Guid ColumnId,
    string Title,
    double Position,
    DateTimeOffset UpdatedAt,
    /// <summary>
    /// Cuántos trazos tiene el lienzo de la tarjeta.
    /// </summary>
    /// <remarks>
    /// Se envía con el tablero para poder marcar en la tarjeta que contiene
    /// notas a mano sin tener que descargar la tinta de todas ellas, que es
    /// con diferencia lo más pesado del modelo.
    /// </remarks>
    int StrokeCount);

public record CreateBoardRequest(string Title);

public record UpdateBoardRequest(string Title);

public record CreateColumnRequest(string Title, double? Position);

public record UpdateColumnRequest(string? Title, double? Position);
