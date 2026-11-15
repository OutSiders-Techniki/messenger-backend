FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Messenger.Api/Messenger.Api.csproj", "src/Messenger.Api/"]
COPY ["src/Messenger.Application/Messenger.Application.csproj", "src/Messenger.Application/"]
COPY ["src/Messenger.Domain/Messenger.Domain.csproj", "src/Messenger.Domain/"]
COPY ["src/Messenger.Infrastructure/Messenger.Infrastructure.csproj", "src/Messenger.Infrastructure/"]
RUN dotnet restore "src/Messenger.Api/Messenger.Api.csproj"
COPY . .
WORKDIR "/src/src/Messenger.Api"
RUN dotnet build "./Messenger.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Messenger.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Messenger.Api.dll"]
