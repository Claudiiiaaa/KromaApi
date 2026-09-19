# Imagen para desplegar en Render (su plan gratuito no tiene runtime nativo de
# .NET, pero sí acepta Docker).
#
# Va en dos etapas a propósito. La primera compila con el SDK completo, que pesa
# más de un giga; la segunda copia solo el resultado sobre la imagen de
# ejecución, mucho más pequeña. Sin esa separación, cada despliegue subiría el
# compilador entero al servidor.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# El .csproj se copia y se restaura antes que el resto del código. Docker cachea
# cada paso, así que mientras no cambien las dependencias, las compilaciones
# siguientes se saltan la descarga de paquetes entera.
COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./

# Render enruta el tráfico al puerto que exponga el contenedor.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Se ejecuta como usuario sin privilegios: si alguien lograse colarse por un
# fallo de la aplicación, no tendría permisos de administrador dentro del
# contenedor.
USER $APP_UID

ENTRYPOINT ["dotnet", "KromaApi.dll"]
