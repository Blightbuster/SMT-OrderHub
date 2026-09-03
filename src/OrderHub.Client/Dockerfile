# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first to leverage Docker layer caching for restore.
COPY ["src/OrderHub.Domain/OrderHub.Domain.csproj", "src/OrderHub.Domain/"]
COPY ["src/OrderHub.Application/OrderHub.Application.csproj", "src/OrderHub.Application/"]
COPY ["src/OrderHub.Client/OrderHub.Client.csproj", "src/OrderHub.Client/"]
RUN dotnet restore "src/OrderHub.Client/OrderHub.Client.csproj"

# Copy the rest and publish the WASM client to static files.
COPY ["src/", "src/"]
RUN dotnet publish "src/OrderHub.Client/OrderHub.Client.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

# ---- Hosting stage: nginx serves the static WASM assets ----
FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html

# SPA fallback: route unknown paths to index.html so client-side routing works.
COPY --from=build /app/publish/wwwroot .
COPY src/OrderHub.Client/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80