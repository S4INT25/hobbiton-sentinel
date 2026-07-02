FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# ── React SPA build ──
FROM node:24-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ .
# outDir in vite.config.ts points outside /web — build to a local dist instead
RUN npx vite build --outDir dist

# ── .NET API build ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Sentinel.Api/Sentinel.Api.csproj", "src/Sentinel.Api/"]
RUN dotnet restore "src/Sentinel.Api/Sentinel.Api.csproj"
COPY . .
WORKDIR "/src/src/Sentinel.Api"
RUN dotnet build "./Sentinel.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Sentinel.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=web /web/dist ./wwwroot
ENTRYPOINT ["dotnet", "Sentinel.dll"]
