# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

XUnityToolkit-WebUI is a web-based tool for one-click installation of XUnity.AutoTranslator and BepInEx plugins into Unity games. It features a Vue 3 frontend served by an ASP.NET Core backend.

## Build Commands

```bash
# Build backend
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# Run backend (serves the web UI on http://localhost:5000)
dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# Build frontend (output to XUnityToolkit-WebUI/wwwroot/)
cd xunity-webui && npm run build

# Frontend dev server (proxies API to backend)
cd xunity-webui && npm run dev

# Type-check frontend
cd xunity-webui && npx vue-tsc --noEmit
```

## Architecture

- **Backend:** ASP.NET Core Minimal API (.NET 10.0, Windows Forms for native dialogs)
- **Frontend:** Vue 3 + TypeScript + Naive UI + Pinia (in `xunity-webui/`)
- **Real-time:** SignalR for install progress updates
- **Persistence:** JSON file at `%APPDATA%/XUnityToolkit/library.json`

## Code Conventions

- **Target Framework:** net10.0-windows
- **Root Namespace:** XUnityToolkit_WebUI
- **Nullable reference types:** Enabled
- **Implicit usings:** Enabled
- **Frontend:** Vue 3 Composition API with `<script setup lang="ts">`
