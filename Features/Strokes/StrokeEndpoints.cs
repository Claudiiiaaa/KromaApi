using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using KromaApi.Common;
using KromaApi.Domain;
using KromaApi.Infrastructure;

namespace KromaApi.Features.Strokes;

public static class StrokeEndpoints
{
    /// <summary>Trazos que se admiten en una sola petición.</summary>
    private const int MaxStrokesPerRequest = 500;

    /// <summary>
    /// Valores que puede tener un trazo, es decir 5.000 puntos.
    /// </summary>
    /// <remarks>
    /// Estos límites no son burocracia: sin ellos, un fallo en el cliente o
    /// alguien con malas intenciones podría llenar los 0,5 GB gratuitos de Neon
    /// con una sola petición y dejar la app inservible hasta el mes siguiente.
    /// Cinco mil puntos son varios minutos de trazo continuo sin levantar el
    /// lápiz, así que ningún trazo real se acerca.
    /// </remarks>
    private const int MaxValuesPerStroke = 20_000;

    private static readonly HashSet<string> ValidTools =
        new(StringComparer.OrdinalIgnoreCase) { "pen", "highlighter", "eraser" };

    public static void MapStrokeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cards/{cardId:guid}/strokes")
            .WithTags("Strokes")
            .RequireAuthorization();

        group.MapGet("/", GetStrokes);
        group.MapPost("/sync", SyncStrokes);

        return;
    }

    private static async Task<IResult> GetStrokes(
        Guid cardId,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        if (!await CardBelongsToUserAsync(db, cardId, userId, ct))
            return Results.NotFound();

        var strokes = await db.Strokes
            .Where(s => s.CardId == cardId)
            // El orden de creación es el orden de pintado, y determina qué
            // trazo queda encima de cuál. Sin este `OrderBy`, Postgres puede
            // devolver las filas en cualquier orden y un subrayado acabaría
            // tapando el texto al recargar la nota.
            .OrderBy(s => s.CreatedAt)
            .Select(s => new StrokeDto(
                s.Id, s.Tool, s.ColorArgb, s.BaseWidth, s.SimulatePressure, s.Points))
            .ToListAsync(ct);

        return Results.Ok(strokes);
    }

    private static async Task<IResult> SyncStrokes(
        Guid cardId,
        SyncStrokesRequest request,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        if (!await CardBelongsToUserAsync(db, cardId, userId, ct))
            return Results.NotFound();

        var added = request.Added ?? [];
        var deletedIds = request.DeletedIds ?? [];

        if (added.Count > MaxStrokesPerRequest)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["added"] = [$"Máximo {MaxStrokesPerRequest} trazos por petición."],
            });

        if (Validate(added) is { } validationError)
            return Results.ValidationProblem(validationError);

        var deletedCount = 0;
        if (deletedIds.Count > 0)
        {
            deletedCount = await db.Strokes
                .Where(s => s.CardId == cardId && deletedIds.Contains(s.Id))
                .ExecuteDeleteAsync(ct);
        }

        var insertedCount = 0;
        if (added.Count > 0)
        {
            var incomingIds = added.Select(s => s.Id).ToList();

            // La búsqueda de identificadores repetidos va **sin filtrar por
            // tarjeta**, y es importante: la clave primaria de `strokes` es el
            // identificador a secas, así que uno que ya exista en otra tarjeta
            // colisiona igual. Filtrando solo dentro de esta, esos casos se
            // colaban hasta Postgres y salían como un error 500 incomprensible.
            var existing = await db.Strokes
                .Where(s => incomingIds.Contains(s.Id))
                .Select(s => new { s.Id, s.CardId })
                .ToListAsync(ct);

            // Un identificador que pertenece a otra tarjeta es un fallo del
            // cliente, no un reintento. Se dice claramente en lugar de ignorarlo
            // en silencio, que escondería el error hasta que faltase tinta.
            var foreignIds = existing
                .Where(e => e.CardId != cardId)
                .Select(e => e.Id)
                .ToList();

            if (foreignIds.Count > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["added"] =
                    [
                        "Algunos trazos ya existen en otra tarjeta: " +
                        string.Join(", ", foreignIds),
                    ],
                });
            }

            // Los que ya están en esta misma tarjeta se descartan, y eso es lo
            // que hace la operación *idempotente*: si la red se cae después de
            // guardar pero antes de que llegue la respuesta, el cliente
            // reintenta con los mismos identificadores y no se duplica nada.
            var alreadyHere = existing.Select(e => e.Id).ToHashSet();

            var newStrokes = added
                .Where(dto => !alreadyHere.Contains(dto.Id))
                .Select(dto => new Stroke
                {
                    Id = dto.Id,
                    CardId = cardId,
                    Tool = dto.Tool.ToLowerInvariant(),
                    ColorArgb = dto.ColorArgb,
                    BaseWidth = dto.BaseWidth,
                    SimulatePressure = dto.SimulatePressure,
                    Points = dto.Points,
                })
                .ToList();

            db.Strokes.AddRange(newStrokes);
            insertedCount = newStrokes.Count;
        }

        if (insertedCount > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Queda una rendija entre la consulta anterior y el guardado: si
                // dos peticiones con el mismo trazo llegan a la vez, ambas lo
                // ven libre y una de las dos choca. Se responde 409 para que el
                // cliente sepa que puede reintentar, en vez de un 500 que
                // parece un fallo del servidor.
                return Results.Conflict(new
                {
                    message = "Otro envío guardó esos trazos a la vez. "
                        + "Vuelve a intentarlo.",
                });
            }
        }

        if (insertedCount > 0 || deletedCount > 0)
            await TouchCardAsync(db, cardId, ct);

        return Results.Ok(new SyncStrokesResponse(
            insertedCount, deletedCount, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Comprueba que los trazos entrantes tienen forma válida.
    /// </summary>
    /// <remarks>
    /// Lo que llega del cliente nunca se da por bueno, ni siquiera siendo
    /// nuestro propio cliente: cualquiera puede llamar a la API directamente.
    /// Un array de puntos con longitud que no sea múltiplo de cuatro se
    /// guardaría sin protestar y reventaría al dibujarlo semanas después, con
    /// el origen del problema ya imposible de rastrear.
    /// </remarks>
    private static Dictionary<string, string[]>? Validate(List<StrokeDto> strokes)
    {
        foreach (var stroke in strokes)
        {
            if (!ValidTools.Contains(stroke.Tool))
                return new() { ["tool"] = [$"Herramienta desconocida: {stroke.Tool}."] };

            if (stroke.Points is null || stroke.Points.Length == 0)
                return new() { ["points"] = ["Un trazo no puede estar vacío."] };

            if (stroke.Points.Length % Stroke.ValuesPerPoint != 0)
                return new()
                {
                    ["points"] =
                    [
                        "Los puntos van de cuatro en cuatro (x, y, presión, tiempo).",
                    ],
                };

            if (stroke.Points.Length > MaxValuesPerStroke)
                return new() { ["points"] = ["El trazo tiene demasiados puntos."] };

            if (stroke.BaseWidth <= 0 || stroke.BaseWidth > 500)
                return new() { ["baseWidth"] = ["Grosor fuera de rango."] };
        }

        return null;
    }

    private static Task<bool> CardBelongsToUserAsync(
        KromaDbContext db, Guid cardId, Guid userId, CancellationToken ct) =>
        db.Cards.AnyAsync(
            c => c.Id == cardId && c.Column!.Board!.OwnerId == userId, ct);

    private static Task TouchCardAsync(
        KromaDbContext db, Guid cardId, CancellationToken ct) =>
        db.Cards
            .Where(c => c.Id == cardId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.UpdatedAt, DateTimeOffset.UtcNow), ct);
}
