# Build from the repository root: docker build -f src/Accounting.Worker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY Directory.Build.props ./
COPY src/Accounting.Contracts/Accounting.Contracts.csproj src/Accounting.Contracts/
COPY src/Accounting.Messaging/Accounting.Messaging.csproj src/Accounting.Messaging/
COPY src/Accounting.Worker/Accounting.Worker.csproj src/Accounting.Worker/
RUN dotnet restore src/Accounting.Worker/Accounting.Worker.csproj
COPY src/ src/
RUN dotnet publish src/Accounting.Worker/Accounting.Worker.csproj --no-restore -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Accounting.Worker.dll"]
