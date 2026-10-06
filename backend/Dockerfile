# syntax=docker/dockerfile:1.7

ARG DOTNET_SDK_VERSION=10.0.302
ARG DOTNET_RUNTIME_VERSION=10.0.10

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Inventory.Api/Inventory.Api.csproj src/Inventory.Api/
COPY src/Inventory.Application/Inventory.Application.csproj src/Inventory.Application/
COPY src/Inventory.Contracts/Inventory.Contracts.csproj src/Inventory.Contracts/
COPY src/Inventory.Domain/Inventory.Domain.csproj src/Inventory.Domain/
COPY src/Inventory.Infrastructure/Inventory.Infrastructure.csproj src/Inventory.Infrastructure/
COPY src/Inventory.Infrastructure.PostgresMigrations/Inventory.Infrastructure.PostgresMigrations.csproj src/Inventory.Infrastructure.PostgresMigrations/
RUN dotnet restore src/Inventory.Api/Inventory.Api.csproj

COPY src/ src/
RUN dotnet publish src/Inventory.Api/Inventory.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS final
WORKDIR /app

COPY --from=build /app/publish ./
RUN mkdir -p /var/lib/inventory/keys /var/lib/inventory/backups \
    && chown -R "$APP_UID":"$APP_UID" /var/lib/inventory /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Inventory.Api.dll"]
