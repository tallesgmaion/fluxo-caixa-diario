FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG SERVICE
WORKDIR /project
COPY Directory.Build.props .
COPY src/Core/Core.csproj src/Core/
COPY src/${SERVICE}/${SERVICE}.csproj src/${SERVICE}/
RUN dotnet restore src/${SERVICE}/${SERVICE}.csproj
COPY src/ src/
COPY docs/assets/carrefour-banco*.png docs/assets/
RUN dotnet publish src/${SERVICE}/${SERVICE}.csproj -c Release --no-restore -o /out
RUN printf '#!/bin/sh\nexec dotnet /app/%s.dll "$@"\n' "$SERVICE" > /out/start && chmod +x /out/start

FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app
COPY --from=build /out/ .
USER $APP_UID
ENTRYPOINT ["/app/start"]
