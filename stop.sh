#!/usr/bin/env bash
echo "Stopping all Alteva Crawler processes..."
fuser -k 5000/tcp 2>/dev/null || true
fuser -k 5173/tcp 2>/dev/null || true
pkill -f AltevaCrawler.Api 2>/dev/null || true
pkill -f AltevaCrawler.Worker 2>/dev/null || true
echo "Ports 5000 and 5173 are clean."
