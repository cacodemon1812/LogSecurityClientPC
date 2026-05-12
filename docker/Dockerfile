FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/PolicyCollector.Backend/PolicyCollector.Backend.csproj", "src/PolicyCollector.Backend/"]
COPY ["src/PolicyCollector.Models/PolicyCollector.Models.csproj", "src/PolicyCollector.Models/"]
RUN dotnet restore "src/PolicyCollector.Backend/PolicyCollector.Backend.csproj"
COPY . .
WORKDIR "/src/src/PolicyCollector.Backend"
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PolicyCollector.Backend.dll"]
