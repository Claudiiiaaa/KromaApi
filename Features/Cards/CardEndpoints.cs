using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using KromaApi.Common;
using KromaApi.Domain;
using KromaApi.Features.Boards;
using KromaApi.Infrastructure;

namespace KromaApi.Features.Cards;

public static class CardEndpoints
{
    private const double PositionGap = 1000;

    public static void MapCardEndpoints(this IEndpointRouteBuilder app)
    {
        var columns = app.MapGroup("/columns")
            .WithTags("Columns")
            .RequireAuthorization();

        columns.MapPost("/{columnId:guid}/cards", CreateCard);
        columns.MapPut("/{columnId:guid}", UpdateColumn);
        columns.MapDelete("/{columnId:guid}", DeleteColumn);

        var cards = app.MapGroup("/cards")
            .WithTags("Cards")
            .RequireAuthorization();

        cards.MapPut("/{cardId:guid}", UpdateCard);
        cards.MapPost("/{cardId:guid}/move", MoveCard);
        cards.MapDelete("/{cardId:guid}", DeleteCard);
    }

    private static async Task<IResult> CreateCard(
        Guid columnId,
        CreateCardRequest request,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        // Se navega desde la columna hasta el dueño del tablero en la misma
        // consulta. EF lo traduce a un JOIN, así que comprobar el permiso no
        // cuesta un viaje extra a la base de datos.
        var column = await db.Columns
            .Where(c => c.Id == columnId && c.Board!.OwnerId == userId)
            .Select(c => new { c.Id, c.BoardId })
            .FirstOrDefaultAsync(ct);

        if (column is null) return Results.NotFound();

        var title = request.Title?.Trim() ?? string.Empty;

        var position = request.Position ?? await NextPositionAsync(db, columnId, ct);

        var card = new Card
        {
            ColumnId = columnId,
            // Una tarjeta sin título es válida: lo normal al escribir a mano es
            // crear la tarjeta y ponerse a dibujar. Se le da un texto neutro
            // para que el tablero no muestre un hueco vacío.
            Title = string.IsNullOrWhiteSpace(title) ? "Nueva tarjeta" : title,
            Position = position,
        };

        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);
        await TouchBoardAsync(db, column.BoardId, ct);

        return Results.Created(
            $"/cards/{card.Id}",
            new CardDto(card.Id, card.ColumnId, card.Title, card.Position,
                card.UpdatedAt, 0));
    }

    private static async Task<IResult> UpdateCard(
        Guid cardId,
        UpdateCardRequest request,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        var card = await db.Cards
            .Include(c => c.Column)
            .FirstOrDefaultAsync(
                c => c.Id == cardId && c.Column!.Board!.OwnerId == userId, ct);

        if (card is null) return Results.NotFound();

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["La tarjeta necesita un título."],
            });

        card.Title = title;
        card.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await TouchBoardAsync(db, card.Column!.BoardId, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> MoveCard(
        Guid cardId,
        MoveCardRequest request,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        var card = await db.Cards
            .Include(c => c.Column)
            .FirstOrDefaultAsync(
                c => c.Id == cardId && c.Column!.Board!.OwnerId == userId, ct);

        if (card is null) return Results.NotFound();

        // La columna de destino se valida aparte: el cliente manda su
        // identificador, y sin comprobarlo se podría mover una tarjeta al
        // tablero de otra persona simplemente cambiando ese valor.
        var target = await db.Columns
            .Where(c => c.Id == request.TargetColumnId && c.Board!.OwnerId == userId)
            .Select(c => new { c.Id, c.BoardId })
            .FirstOrDefaultAsync(ct);

        if (target is null) return Results.NotFound();

        // Y además debe ser del mismo tablero: mover una tarjeta a otro tablero
        // distinto es una operación que la interfaz no ofrece, así que si llega
        // es por error o por manipulación.
        if (target.BoardId != card.Column!.BoardId)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["targetColumnId"] = ["La columna destino es de otro tablero."],
            });

        card.ColumnId = target.Id;
        card.Position = request.Position;
        card.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await TouchBoardAsync(db, target.BoardId, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteCard(
        Guid cardId,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        var deleted = await db.Cards
            .Where(c => c.Id == cardId && c.Column!.Board!.OwnerId == userId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> UpdateColumn(
        Guid columnId,
        UpdateColumnRequest request,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        var column = await db.Columns
            .FirstOrDefaultAsync(
                c => c.Id == columnId && c.Board!.OwnerId == userId, ct);

        if (column is null) return Results.NotFound();

        // Campos opcionales: se puede renombrar sin mover y mover sin
        // renombrar, así que solo se toca lo que venga informado.
        if (request.Title is not null)
        {
            var title = request.Title.Trim();
            if (string.IsNullOrWhiteSpace(title))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["title"] = ["La columna necesita un nombre."],
                });
            column.Title = title;
        }

        if (request.Position is not null) column.Position = request.Position.Value;

        await db.SaveChangesAsync(ct);
        await TouchBoardAsync(db, column.BoardId, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteColumn(
        Guid columnId,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        var deleted = await db.Columns
            .Where(c => c.Id == columnId && c.Board!.OwnerId == userId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    /// <summary>Posición para añadir al final de una columna.</summary>
    private static async Task<double> NextPositionAsync(
        KromaDbContext db, Guid columnId, CancellationToken ct)
    {
        // `MaxAsync` sobre un tipo anulable devuelve null con la columna vacía,
        // en lugar de lanzar como haría la versión no anulable.
        var max = await db.Cards
            .Where(c => c.ColumnId == columnId)
            .MaxAsync(c => (double?)c.Position, ct);

        return (max ?? 0) + PositionGap;
    }

    /// <summary>
    /// Marca el tablero como modificado.
    /// </summary>
    /// <remarks>
    /// Se actualiza con un UPDATE directo en vez de cargar el tablero entero
    /// solo para cambiarle una fecha. Este campo es el que permitirá al cliente
    /// preguntar «¿ha cambiado algo desde la última vez?» sin descargarlo todo.
    ///
    /// Se llama siempre <b>después</b> de <c>SaveChangesAsync</c>, y el orden
    /// importa: <c>ExecuteUpdateAsync</c> va directo a la base de datos sin
    /// pasar por el seguimiento de cambios, así que ejecutarlo antes marcaría
    /// el tablero como modificado aunque el guardado principal acabara fallando.
    /// </remarks>
    private static Task TouchBoardAsync(
        KromaDbContext db, Guid boardId, CancellationToken ct) =>
        db.Boards
            .Where(b => b.Id == boardId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(b => b.UpdatedAt, DateTimeOffset.UtcNow), ct);
}
