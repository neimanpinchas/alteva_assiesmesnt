FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/AltevaCrawler.Shared/AltevaCrawler.Shared.csproj", "src/AltevaCrawler.Shared/"]
COPY ["src/AltevaCrawler.Worker/AltevaCrawler.Worker.csproj", "src/AltevaCrawler.Worker/"]
RUN dotnet restore "src/AltevaCrawler.Worker/AltevaCrawler.Worker.csproj"
COPY . .
WORKDIR "/src/src/AltevaCrawler.Worker"
RUN dotnet publish "AltevaCrawler.Worker.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AltevaCrawler.Worker.dll"]
