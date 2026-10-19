FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/AltevaCrawler.Shared/AltevaCrawler.Shared.csproj", "src/AltevaCrawler.Shared/"]
COPY ["src/AltevaCrawler.Api/AltevaCrawler.Api.csproj", "src/AltevaCrawler.Api/"]
RUN dotnet restore "src/AltevaCrawler.Api/AltevaCrawler.Api.csproj"
COPY . .
WORKDIR "/src/src/AltevaCrawler.Api"
RUN dotnet publish "AltevaCrawler.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AltevaCrawler.Api.dll"]
