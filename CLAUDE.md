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

### Frontend Structure

```
xunity-webui/src/
├── api/            # API client and TypeScript types
├── assets/         # Global CSS (design system variables, animations)
├── components/
│   ├── layout/     # AppShell (sidebar + content layout)
│   ├── config/     # ConfigPanel (translation settings form)
│   └── progress/   # InstallProgressDrawer (SignalR progress)
├── stores/         # Pinia stores (games, install)
├── views/          # LibraryView, GameDetailView
└── router/         # Vue Router config (/ and /games/:id)
```

### Frontend Design System

- **Fonts:** Lexend (headings), DM Sans (body), JetBrains Mono (mono) — loaded via Google Fonts in `index.html`
- **Colors:** Deep dark base (`#0b0b11`), teal accent (`#22d3a7`), violet secondary (`#a78bfa`)
- **CSS Variables:** Defined in `main.css` (`--bg-root`, `--accent`, `--text-1`, `--radius-lg`, `--ease-out`, etc.)
- **Theme:** Naive UI dark theme with comprehensive `GlobalThemeOverrides` in `App.vue`
- **Animations:** Keyframes in `main.css` (`slideUp`, `fadeIn`, `floatIn`, `shimmer`, `breathe`, `pulse`); page transitions via Vue `<Transition name="page">`
- **Layout:** Custom sidebar (230px) + scrollable content area; no NLayout/NLayoutSider
- **Game cards:** Custom HTML/CSS (not NCard) with staggered entrance animations and hover effects

## Code Conventions

- **Target Framework:** net10.0-windows
- **Root Namespace:** XUnityToolkit_WebUI
- **Nullable reference types:** Enabled
- **Implicit usings:** Enabled
- **Frontend:** Vue 3 Composition API with `<script setup lang="ts">`
- **Frontend styling:** Scoped `<style scoped>` per component; use CSS variables from `main.css` for theming
- **Frontend icons:** `@vicons/material` and `@vicons/ionicons5` wrapped in Naive UI `NIcon`
