# Базовый образ для среды выполнения
FROM mcr.microsoft.com/dotnet/runtime:8.0-nanoserver-1809 AS base
WORKDIR /app

# Образ для сборки проекта
FROM mcr.microsoft.com/dotnet/sdk:8.0-nanoserver-1809 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ConsoleApp1.csproj", "."]

# Указываем пользователя ContainerAdministrator
USER ContainerAdministrator

# Восстановление зависимостей
RUN dotnet restore "./ConsoleApp1.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./ConsoleApp1.csproj" -c %BUILD_CONFIGURATION% -o /app/build

# Образ для публикации
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ConsoleApp1.csproj" -c %BUILD_CONFIGURATION% -o /app/publish /p:UseAppHost=false

# Финальный образ
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ConsoleApp1.dll"]
