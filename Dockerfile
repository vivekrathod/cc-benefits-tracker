# Build the API (which serves the web app) and the operator CLI.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Chitragupta.Api -c Release -o /out/api \
 && dotnet publish tools/Chitragupta.Cli -c Release -o /out/cli

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Periods, "today" and the schedule follow the household's time zone, so the zone data must be there.
RUN apt-get update \
 && apt-get install -y --no-install-recommends tzdata \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/api ./
COPY --from=build /out/cli ./cli

# The database and its backups live in /data, which is a mount. Secrets are mounted read-only.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Chitragupta__DataDirectory=/data \
    HOME=/tmp
EXPOSE 8080
ENTRYPOINT ["dotnet", "Chitragupta.Api.dll"]
