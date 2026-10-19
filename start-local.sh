#!/usr/bin/env bash
./stop.sh
echo "Starting Alteva Crawler API and Worker..."
dotnet run --project src/AltevaCrawler.Api/AltevaCrawler.Api.csproj > /dev/null 2>&1 &
dotnet run --project src/AltevaCrawler.Worker/AltevaCrawler.Worker.csproj > /dev/null 2>&1 &
sleep 4
echo "Checking API health on http://localhost:5000/health..."
curl -s http://localhost:5000/health || echo "API starting up..."
