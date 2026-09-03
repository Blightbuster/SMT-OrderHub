# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first to leverage Docker layer caching for restore.
COPY ["src/OrderHub.Domain/OrderHub.Domain.csproj", "src/OrderHub.Domain/"]
COPY ["src/OrderHub.Application/OrderHub.Application.csproj", "src/OrderHub.Application/"]
COPY ["src/OrderHub.Infrastructure/OrderHub.Infrastructure.csproj", "src/OrderHub.Infrastructure/"]
COPY ["src/OrderHub.Api/OrderHub.Api.csproj", "src/OrderHub.Api/"]
RUN dotnet restore "src/OrderHub.Api/OrderHub.Api.csproj"

# Copy the rest and publish.
COPY ["src/", "src/"]
RUN dotnet publish "src/OrderHub.Api/OrderHub.Api.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# SQLite database lives on a mounted volume (see docker-compose.yml).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrderHub.Api.dll"]