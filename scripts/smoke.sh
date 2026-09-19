#!/usr/bin/env bash
# Prueba de humo de la API de Kroma: registro, tablero, tarjeta y tinta.
set -u
API="http://localhost:5212"
RUN=$(date +%s)
EMAIL="claudia+$RUN@example.com"

field() { grep -o "\"$1\":\"[^\"]*\"" | head -1 | cut -d'"' -f4; }
check() { # check <esperado> <obtenido> <descripción>
  if [ "$1" = "$2" ]; then echo "   OK  $3 ($2)"; else echo "   MAL $3: esperaba $1, recibí $2"; fi
}

# Identificadores únicos por ejecución. Con valores fijos, una segunda pasada
# chocaría con los trazos que dejó la anterior: es exactamente el caso que
# destapó el fallo de la clave primaria duplicada.
STROKE1="11111111-1111-7111-8111-$(printf '%012d' "$RUN")"
STROKE2="22222222-2222-7222-8222-$(printf '%012d' "$RUN")"
STROKE3="33333333-3333-7333-8333-$(printf '%012d' "$RUN")"

echo "== 1. Registro ($EMAIL)"
REG=$(curl -s -X POST "$API/auth/register" -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"unaClaveSegura1\",\"displayName\":\"Claudia\"}")
TOKEN=$(echo "$REG" | field token)
if [ -z "$TOKEN" ]; then echo "   FALLO: $REG"; exit 1; fi
echo "   OK  token recibido (${#TOKEN} caracteres)"

AUTH="Authorization: Bearer $TOKEN"

echo "== 2. Contraseña corta debe rechazarse"
SHORT=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API/auth/register" \
  -H "Content-Type: application/json" \
  -d '{"email":"x@y.com","password":"corta","displayName":"X"}')
check 400 "$SHORT" "contraseña corta"

echo "== 3. Sin token debe dar 401"
check 401 "$(curl -s -o /dev/null -w '%{http_code}' "$API/boards")" "acceso sin token"

echo "== 4. El registro crea un tablero con tres columnas"
BOARDS=$(curl -s "$API/boards" -H "$AUTH")
BOARD_ID=$(echo "$BOARDS" | field id)
DETAIL=$(curl -s "$API/boards/$BOARD_ID" -H "$AUTH")
COLUMN_ID=$(echo "$DETAIL" | grep -o '"id":"[^"]*"' | sed -n '2p' | cut -d'"' -f4)
COLUMNS=$(echo "$DETAIL" | grep -o '"title":"[^"]*"' | wc -l)
check 4 "$COLUMNS" "títulos (tablero + 3 columnas)"
echo "$DETAIL" | grep -o '"title":"[^"]*"' | tr '\n' ' '; echo

echo "== 5. Crear tarjeta"
CARD=$(curl -s -X POST "$API/columns/$COLUMN_ID/cards" -H "$AUTH" \
  -H "Content-Type: application/json" -d '{"title":"Comprar pinceles"}')
CARD_ID=$(echo "$CARD" | field id)
echo "   OK  tarjeta $CARD_ID"

echo "== 6. Subir dos trazos"
SYNC=$(curl -s -X POST "$API/cards/$CARD_ID/strokes/sync" -H "$AUTH" \
  -H "Content-Type: application/json" \
  -d "{\"added\":[
        {\"id\":\"$STROKE1\",\"tool\":\"pen\",\"colorArgb\":-16777216,
         \"baseWidth\":3,\"simulatePressure\":false,
         \"points\":[0,0,0.5,0, 10,10,0.6,16, 20,5,0.4,32]},
        {\"id\":\"$STROKE2\",\"tool\":\"highlighter\",\"colorArgb\":-256,
         \"baseWidth\":18,\"simulatePressure\":true,
         \"points\":[0,50,0.5,0, 100,50,0.5,20]}
      ],\"deletedIds\":[]}")
check 2 "$(echo "$SYNC" | grep -o '"added":[0-9]*' | cut -d: -f2)" "trazos insertados"

echo "== 7. Reenviar los mismos trazos (idempotencia)"
AGAIN=$(curl -s -X POST "$API/cards/$CARD_ID/strokes/sync" -H "$AUTH" \
  -H "Content-Type: application/json" \
  -d "{\"added\":[
        {\"id\":\"$STROKE1\",\"tool\":\"pen\",\"colorArgb\":-16777216,
         \"baseWidth\":3,\"simulatePressure\":false,
         \"points\":[0,0,0.5,0, 10,10,0.6,16]}
      ],\"deletedIds\":[]}")
check 0 "$(echo "$AGAIN" | grep -o '"added":[0-9]*' | cut -d: -f2)" "reenvío no duplica"

echo "== 8. Un identificador ya usado en OTRA tarjeta debe dar 400, no 500"
# Este es el caso que antes llegaba hasta Postgres y salía como error 500.
CARD2=$(curl -s -X POST "$API/columns/$COLUMN_ID/cards" -H "$AUTH" \
  -H "Content-Type: application/json" -d '{"title":"Otra tarjeta"}')
CARD2_ID=$(echo "$CARD2" | field id)
DUP=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API/cards/$CARD2_ID/strokes/sync" \
  -H "$AUTH" -H "Content-Type: application/json" \
  -d "{\"added\":[{\"id\":\"$STROKE1\",\"tool\":\"pen\",\"colorArgb\":-16777216,
       \"baseWidth\":3,\"simulatePressure\":false,
       \"points\":[0,0,0.5,0, 5,5,0.5,16]}],\"deletedIds\":[]}")
check 400 "$DUP" "identificador de otra tarjeta"

echo "== 9. Puntos mal formados (5 valores, no múltiplo de 4)"
BAD=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API/cards/$CARD_ID/strokes/sync" \
  -H "$AUTH" -H "Content-Type: application/json" \
  -d "{\"added\":[{\"id\":\"$STROKE3\",\"tool\":\"pen\",
       \"colorArgb\":-16777216,\"baseWidth\":3,\"simulatePressure\":false,
       \"points\":[0,0,0.5,0,99]}],\"deletedIds\":[]}")
check 400 "$BAD" "puntos mal formados"

echo "== 10. Herramienta desconocida"
TOOL=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API/cards/$CARD_ID/strokes/sync" \
  -H "$AUTH" -H "Content-Type: application/json" \
  -d "{\"added\":[{\"id\":\"$STROKE3\",\"tool\":\"aerografo\",
       \"colorArgb\":-16777216,\"baseWidth\":3,\"simulatePressure\":false,
       \"points\":[0,0,0.5,0]}],\"deletedIds\":[]}")
check 400 "$TOOL" "herramienta inválida"

echo "== 11. Leer los trazos guardados"
GOT=$(curl -s "$API/cards/$CARD_ID/strokes" -H "$AUTH")
check 2 "$(echo "$GOT" | grep -o '"id":"' | wc -l)" "trazos leídos"

echo "== 12. Borrar un trazo"
DEL=$(curl -s -X POST "$API/cards/$CARD_ID/strokes/sync" -H "$AUTH" \
  -H "Content-Type: application/json" \
  -d "{\"added\":[],\"deletedIds\":[\"$STROKE2\"]}")
check 1 "$(echo "$DEL" | grep -o '"deleted":[0-9]*' | cut -d: -f2)" "trazo borrado"

echo "== 13. Mover la tarjeta a la columna 'Hecho'"
DONE_COLUMN=$(echo "$DETAIL" | grep -o '"id":"[^"]*"' | sed -n '4p' | cut -d'"' -f4)
MOVED=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API/cards/$CARD_ID/move" \
  -H "$AUTH" -H "Content-Type: application/json" \
  -d "{\"targetColumnId\":\"$DONE_COLUMN\",\"position\":1500}")
check 204 "$MOVED" "mover tarjeta"

echo "== 14. Aislamiento entre cuentas"
OTHER=$(curl -s -X POST "$API/auth/register" -H "Content-Type: application/json" \
  -d "{\"email\":\"intrusa+$RUN@example.com\",\"password\":\"otraClave12345\",\"displayName\":\"Intrusa\"}")
OTHER_TOKEN=$(echo "$OTHER" | field token)
check 404 "$(curl -s -o /dev/null -w '%{http_code}' "$API/cards/$CARD_ID/strokes" \
  -H "Authorization: Bearer $OTHER_TOKEN")" "leer tinta ajena"
check 404 "$(curl -s -o /dev/null -w '%{http_code}' "$API/boards/$BOARD_ID" \
  -H "Authorization: Bearer $OTHER_TOKEN")" "leer tablero ajeno"

echo
echo "FIN"
