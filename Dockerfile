# Trino Supply — Foundation API (.NET 9) + frontend React (Vite)
# Build context: raiz do repositório

# 1) Frontend React: o build gera src/backend/.../wwwroot por inteiro (base /)
FROM node:22-alpine AS frontend
WORKDIR /src/frontend
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/frontend/ ./
RUN npm run build

# 2) API .NET, com o build do React já dentro do wwwroot
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/backend/Foundation/TrinoSupply.Foundation.Api/TrinoSupply.Foundation.Api.csproj Foundation/TrinoSupply.Foundation.Api/
RUN dotnet restore Foundation/TrinoSupply.Foundation.Api/TrinoSupply.Foundation.Api.csproj
COPY src/backend/ .
COPY --from=frontend /src/backend/Foundation/TrinoSupply.Foundation.Api/wwwroot Foundation/TrinoSupply.Foundation.Api/wwwroot
RUN dotnet publish Foundation/TrinoSupply.Foundation.Api -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
# fontconfig é exigido pelo QuestPDF (PDF da Ordem de Compra)
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080
# O Railway injeta PORT; localmente o padrão é 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet TrinoSupply.Foundation.Api.dll"]
