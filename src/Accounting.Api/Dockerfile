# Build from the repository root: docker build -f src/Accounting.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY Directory.Build.props ./
COPY src/Accounting.Contracts/Accounting.Contracts.csproj src/Accounting.Contracts/
COPY src/Accounting.Messaging/Accounting.Messaging.csproj src/Accounting.Messaging/
COPY src/Accounting.Api/Accounting.Api.csproj src/Accounting.Api/
RUN dotnet restore src/Accounting.Api/Accounting.Api.csproj
COPY src/ src/
RUN dotnet publish src/Accounting.Api/Accounting.Api.csproj --no-restore -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Accounting.Api.dll"]
