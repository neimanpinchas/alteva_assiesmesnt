#!/usr/bin/env python3
"""
Simple zero-dependency Deployment Webhook Listener.
Listens on port 9000 (or PORT env var) and triggers 'git pull' & 'docker compose up -d --build'
when receiving an authorized HTTP POST request.
"""

import os
import sys
import json
import subprocess
from http.server import HTTPServer, BaseHTTPRequestHandler

PORT = int(os.getenv("PORT", "9000"))
WEBHOOK_SECRET = os.getenv("WEBHOOK_SECRET", "change-me-to-a-secure-secret")
PROJECT_DIR = os.getenv("PROJECT_DIR", "/opt/alteva_crawler")

class WebhookHandler(BaseHTTPRequestHandler):
    def _send_json(self, status_code, data):
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps(data).encode("utf-8"))

    def do_POST(self):
        auth_header = self.headers.get("Authorization", "")
        expected_auth = f"Bearer {WEBHOOK_SECRET}"

        if auth_header != expected_auth:
            return self._send_json(401, {"error": "Unauthorized: Invalid secret token"})

        self._send_json(200, {"status": "success", "message": "Deploy trigger received. Pulling from GitHub and rebuilding containers..."})
        
        # Run fetch, hard reset to main, & docker compose rebuild asynchronously
        cmd = f"cd '{PROJECT_DIR}' && git fetch origin && git reset --hard origin/main && (docker compose up -d --build || docker-compose up -d --build)"
        subprocess.Popen(["/bin/bash", "-c", cmd])

    def do_GET(self):
        self._send_json(200, {"status": "ok", "service": "Alteva Crawler Deploy Webhook"})

if __name__ == "__main__":
    HTTPServer.allow_reuse_address = True
    server = HTTPServer(("0.0.0.0", PORT), WebhookHandler)
    print(f"Deploy webhook server running on port {PORT}...")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        sys.exit(0)
