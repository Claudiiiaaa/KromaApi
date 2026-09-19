using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using KromaApi.Common;
using KromaApi.Domain;
using KromaApi.Infrastructure;

namespace KromaApi.Features.Boards;

public static class BoardEndpoints
{
    /// <summary>
    /// Separación entre posiciones consecutivas al crear elementos nuevos.
    /// </summary>
    /// <remarks>
    /// Se dejan huecos amplios a propósito. Cada vez que algo se arrastra entre
    /// otros dos, su posición pasa a ser la media de ambos, y partir de huecos
    /// grandes permite muchísimas reordenaciones antes de que los decimales se
    /// queden sin sitio.
    /// </remarks>
    private const double PositionGap = 1000;

    public static RouteGroupBuilder MapBoardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/boards")
            .WithTags("Boards")
            // Se exige token en todo el grupo de una vez. Protegerlos uno a uno
            // funciona hasta que alguien añade un endpoint y olvida la línea:
            // el fallo no rompe nada, simplemente deja los datos al aire.
            .RequireAuthorization();

        group.MapGet("/", ListBoards);
        group.MapPost("/", CreateBoard);
        group.MapGet("/{boardId:guid}", GetBoard);
        group.MapPut("/{boardId:guid}", UpdateBoard);
        group.MapDelete("/{boardId:guid}", DeleteBoard);

        group.MapPost("/{boardId:guid}/columns", CreateColumn);

        return group;
    }

    private static async Task<IResult> ListBoards(
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        var boards = await db.Boards
            .Where(b => b.OwnerId == userId)
            .OrderByDescending(b => b.UpdatedAt)
            .Select(b => new BoardSummaryDto(
                b.Id,
                b.Title,
                b.UpdatedAt,
                // Se cuenta en la base de datos, no en memoria: así viaja un
                // número en lugar de todas las tarjetas de todos los tableros.
                b.Columns.SelectMany(c => c.Cards).Count()))
            .ToListAsync(ct);

        return Results.Ok(boards);
    }

    private static async Task<IResult> CreateBoard(
        CreateBoardRequest request,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["El tablero necesita un nombre."],
            });

        var board = new Board
        {
            OwnerId = principal.GetUserId(),
            Title = title,
            Columns =
            [
                new BoardColumn { Title = "Por hacer", Position = PositionGap },
                new BoardColumn { Title = "Haciendo", Position = PositionGap * 2 },
                new BoardColumn { Title = "Hecho", Position = PositionGap * 3 },
            ],
        };

        db.Boards.Add(board);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/boards/{board.Id}",
            new BoardSummaryDto(board.Id, board.Title, board.UpdatedAt, 0));
    }

    private static async Task<IResult> GetBoard(
        Guid boardId,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        var board = await db.Boards
            // El filtro por dueño va en la misma consulta que la búsqueda por
            // id, no en un `if` posterior. Es la diferencia entre no encontrar
            // el tablero ajeno y encontrarlo para después decidir si enseñarlo.
            .Where(b => b.Id == boardId && b.OwnerId == userId)
            .Select(b => new BoardDetailDto(
                b.Id,
                b.Title,
                b.UpdatedAt,
                b.Columns
                    .OrderBy(c => c.Position)
                    .Select(c => new ColumnDto(
                        c.Id,
                        c.Title,
                        c.Position,
                        c.Cards
                            .OrderBy(card => card.Position)
                            .Select(card => new CardDto(
                                card.Id,
                                card.ColumnId,
                                card.Title,
                                card.Position,
                                card.UpdatedAt,
                                card.Strokes.Count))
                            .ToList()))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        // 404 y no 403 cuando el tablero es de otra persona: un 403 confirmaría
        // que ese identificador existe, que ya es información de más.
        return board is null ? Results.NotFound() : Results.Ok(board);
    }

    private static async Task<IResult> UpdateBoard(
        Guid boardId,
        UpdateBoardRequest request,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        var board = await db.Boards
            .FirstOrDefaultAsync(b => b.Id == boardId && b.OwnerId == userId, ct);

        if (board is null) return Results.NotFound();

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["El tablero necesita un nombre."],
            });

        board.Title = title;
        board.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteBoard(
        Guid boardId,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        // `ExecuteDeleteAsync` manda un único DELETE a Postgres en vez de
        // traerse la entidad para borrarla después. Las columnas, tarjetas y
        // trazos caen solos por el borrado en cascada de las claves ajenas.
        var deleted = await db.Boards
            .Where(b => b.Id == boardId && b.OwnerId == userId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> CreateColumn(
        Guid boardId,
        CreateColumnRequest request,
        ClaimsPrincipal principal,
        KromaDbContext db,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();

        var board = await db.Boards
            .Include(b => b.Columns)
            .FirstOrDefaultAsync(b => b.Id == boardId && b.OwnerId == userId, ct);

        if (board is null) return Results.NotFound();

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["La columna necesita un nombre."],
            });

        // Sin posición indicada, va al final.
        var position = request.Position
            ?? (board.Columns.Count == 0
                ? PositionGap
                : board.Columns.Max(c => c.Position) + PositionGap);

        var column = new BoardColumn
        {
            BoardId = boardId,
            Title = title,
            Position = position,
        };

        db.Columns.Add(column);
        board.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/boards/{boardId}/columns/{column.Id}",
            new ColumnDto(column.Id, column.Title, column.Position, []));
    }
}
