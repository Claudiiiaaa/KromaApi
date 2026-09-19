using Microsoft.EntityFrameworkCore;
using KromaApi.Domain;

namespace KromaApi.Infrastructure;

/// <summary>
/// La sesión con la base de datos: por aquí pasa toda lectura y escritura.
/// </summary>
public class KromaDbContext(DbContextOptions<KromaDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardColumn> Columns => Set<BoardColumn>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Stroke> Strokes => Set<Stroke>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            // Índice único sobre el correo: es la base de datos la que impide
            // dos cuentas iguales, no el código. Comprobarlo solo en C# deja
            // una rendija: entre la comprobación y el guardado puede colarse
            // otro registro simultáneo con el mismo correo.
            entity.HasIndex(u => u.Email).IsUnique();

            entity.Property(u => u.Email).HasMaxLength(256);
            entity.Property(u => u.DisplayName).HasMaxLength(100);
            entity.Property(u => u.PasswordHash).HasMaxLength(512);
        });

        modelBuilder.Entity<Board>(entity =>
        {
            entity.Property(b => b.Title).HasMaxLength(200);

            entity.HasOne(b => b.Owner)
                .WithMany(u => u.Boards)
                .HasForeignKey(b => b.OwnerId)
                // Al borrar una cuenta se lleva por delante sus tableros. Sin
                // esto quedarían filas huérfanas apuntando a un usuario que ya
                // no existe.
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(b => b.OwnerId);
        });

        modelBuilder.Entity<BoardColumn>(entity =>
        {
            entity.Property(c => c.Title).HasMaxLength(100);

            entity.HasOne(c => c.Board)
                .WithMany(b => b.Columns)
                .HasForeignKey(c => c.BoardId)
                .OnDelete(DeleteBehavior.Cascade);

            // Índice compuesto por tablero y posición: la consulta que más se
            // va a repetir es «dame las columnas de este tablero en orden», y
            // así Postgres las devuelve ya ordenadas sin pasar por un sort.
            entity.HasIndex(c => new { c.BoardId, c.Position });
        });

        modelBuilder.Entity<Card>(entity =>
        {
            entity.Property(c => c.Title).HasMaxLength(500);

            entity.HasOne(c => c.Column)
                .WithMany(col => col.Cards)
                .HasForeignKey(c => c.ColumnId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => new { c.ColumnId, c.Position });
        });

        modelBuilder.Entity<Stroke>(entity =>
        {
            entity.Property(s => s.Tool).HasMaxLength(20);

            // `real[]` nativo de Postgres. Npgsql mapea float[] a este tipo sin
            // conversiones intermedias ni serialización JSON.
            entity.Property(s => s.Points).HasColumnType("real[]");

            entity.HasOne(s => s.Card)
                .WithMany(c => c.Strokes)
                .HasForeignKey(s => s.CardId)
                .OnDelete(DeleteBehavior.Cascade);

            // Los trazos se leen siempre por tarjeta y en orden de pintado, que
            // coincide con el de creación.
            entity.HasIndex(s => new { s.CardId, s.CreatedAt });
        });
    }
}
