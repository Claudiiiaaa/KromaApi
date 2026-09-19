namespace KromaApi.Domain;

/// <summary>
/// Un trazo de tinta dentro del lienzo de una tarjeta.
/// </summary>
/// <remarks>
/// Es el reflejo exacto de la clase <c>Stroke</c> de la app de Flutter, y esa
/// simetría no es casual: mantener ambos modelos alineados campo a campo es lo
/// que hace que sincronizar sea copiar datos en vez de traducirlos.
/// </remarks>
public class Stroke
{
    /// <summary>
    /// Identificador generado por el <b>cliente</b>, no por la base de datos.
    /// </summary>
    /// <remarks>
    /// Es una decisión deliberada de arquitectura. La app tiene que poder
    /// dibujar sin conexión, así que necesita dar identidad a un trazo sin
    /// preguntarle al servidor. Al subirlo, el servidor acepta ese
    /// identificador tal cual, y como es un UUID no hay riesgo de colisión.
    /// Además vuelve la subida <i>idempotente</i>: si la red falla y el cliente
    /// reintenta, el segundo envío trae el mismo id y se reconoce como repetido
    /// en vez de duplicar el trazo.
    /// </remarks>
    public Guid Id { get; set; }

    public Guid CardId { get; set; }
    public Card? Card { get; set; }

    /// <summary>Herramienta: <c>pen</c>, <c>highlighter</c> o <c>eraser</c>.</summary>
    /// <remarks>
    /// Se guarda como texto y no como número para que la base de datos sea
    /// legible y para que añadir una herramienta nueva no dependa del orden de
    /// una enumeración, que es un contrato demasiado frágil entre dos lenguajes
    /// distintos.
    /// </remarks>
    public required string Tool { get; set; }

    /// <summary>Color en ARGB de 32 bits.</summary>
    public int ColorArgb { get; set; }

    public double BaseWidth { get; set; }

    /// <summary>
    /// Si el grosor debe deducirse de la velocidad en vez de la presión.
    /// </summary>
    public bool SimulatePressure { get; set; }

    /// <summary>
    /// Los puntos, aplanados de cuatro en cuatro: x, y, presión y milisegundos.
    /// </summary>
    /// <remarks>
    /// Se usa un array nativo de Postgres (<c>real[]</c>) en lugar de JSON, y
    /// por dos motivos. Uno, el tamaño: un trazo son cientos de puntos, y
    /// repetir las claves «x», «y», «pressure» y «tMs» en cada uno multiplicaría
    /// por varias veces el espacio ocupado, que con 0,5 GB gratuitos decide
    /// cuántas notas caben. Dos, la precisión de <c>float</c> sobra de largo
    /// para coordenadas de tinta y ocupa la mitad que un <c>double</c>.
    ///
    /// El precio es que la longitud debe ser múltiplo de cuatro, cosa que
    /// valida la API antes de guardar nada.
    /// </remarks>
    public required float[] Points { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Número de puntos reales del trazo.</summary>
    public int PointCount => Points.Length / ValuesPerPoint;

    /// <summary>Valores que ocupa cada punto: x, y, presión y tiempo.</summary>
    public const int ValuesPerPoint = 4;
}
