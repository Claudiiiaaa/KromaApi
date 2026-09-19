# KromaApi

Backend de **Kroma**, la aplicación de notas manuscritas organizadas en tableros.
Hecho con C# y ASP.NET Core sobre PostgreSQL.

## Estructura

```
Domain/          entidades: User, Board, BoardColumn, Card, Stroke
Infrastructure/  DbContext y migraciones de EF Core
Features/        endpoints agrupados por área (Auth, Boards, Cards, Strokes)
Common/          utilidades compartidas
scripts/         pruebas de humo de la API
```

## Configuración

Los secretos **nunca** van en `appsettings.json`, porque ese archivo se sube a Git.
En local se guardan con `dotnet user-secrets`:

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require"
dotnet user-secrets set "Jwt:Key" "<48 caracteres aleatorios>"
```

Neon da la cadena en formato URI (`postgresql://...`), pero Npgsql espera el formato
clave-valor de ADO.NET. Hay que traducirla; no son intercambiables.

En producción, las mismas claves como variables de entorno:
`ConnectionStrings__Postgres` y `Jwt__Key`.

## Ponerlo en marcha

```bash
dotnet run --launch-profile http     # escucha en http://localhost:5212
```

Las migraciones se aplican solas al arrancar. Para hacerlo a mano:

```bash
dotnet ef database update
```

## Probar

Con el servidor arrancado:

```bash
bash scripts/smoke.sh
```

Recorre el flujo completo —registro, tablero, tarjeta, tinta— y comprueba también
que una cuenta no puede ver los datos de otra.

Para peticiones sueltas está `KromaApi.http`, que se ejecuta desde Visual Studio,
Rider o la extensión REST Client de VS Code.

## Decisiones que conviene conocer

- **Las posiciones son decimales.** Mover una tarjeta entre otras dos le asigna la media
  de sus vecinas, así que cada arrastre es un solo `UPDATE` en vez de renumerar la columna.
- **Los puntos de tinta son `real[]` de Postgres**, aplanados de cuatro en cuatro
  (x, y, presión, milisegundos). Ocupa mucho menos que JSON con claves repetidas.
- **Los identificadores de los trazos los genera el cliente** (UUID v7), para poder
  dibujar sin conexión y para que reenviar un trazo no lo duplique.
- **Toda consulta filtra por el dueño** dentro de la propia consulta, no en un `if`
  posterior. Los recursos ajenos devuelven 404 y no 403, para no confirmar que existen.
