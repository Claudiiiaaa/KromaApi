namespace KromaApi.Features.Strokes;

/// <summary>
/// Un trazo tal y como viaja entre la app y la API.
/// </summary>
/// <remarks>
/// Los puntos van aplanados de cuatro en cuatro (x, y, presión, milisegundos)
/// por el mismo motivo que en la base de datos: un objeto con claves por cada
/// punto multiplicaría por varias veces el tamaño de cada petición, y esto se
/// envía por la red de datos del móvil.
/// </remarks>
public record StrokeDto(
    Guid Id,
    string Tool,
    int ColorArgb,
    double BaseWidth,
    bool SimulatePressure,
    float[] Points);

/// <summary>
/// Los cambios de tinta de una tarjeta, en una sola petición.
/// </summary>
/// <remarks>
/// Un único endpoint para añadir y borrar en lugar de uno por operación, y es
/// una decisión de diseño con motivo: al escribir se producen decenas de trazos
/// en segundos, y mandar una petición por cada uno sería desastroso en el móvil
/// —una conexión nueva por trazo, la batería y el arranque en frío de Render de
/// por medio—. Así la app acumula los cambios y los envía juntos.
/// </remarks>
public record SyncStrokesRequest(
    List<StrokeDto>? Added,
    List<Guid>? DeletedIds);

public record SyncStrokesResponse(
    int Added,
    int Deleted,
    DateTimeOffset SyncedAt);
