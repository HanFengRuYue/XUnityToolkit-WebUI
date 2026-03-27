/// <reference types="vite/client" />

import 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    depth?: number
  }
}

// WebView2 host object injected by the WinForms WebView2 control
declare global {
  interface ChromeWebView {
    postMessage(message: string): void
    addEventListener(type: 'message', listener: (event: { data: string }) => void): void
    removeEventListener(type: 'message', listener: (event: { data: string }) => void): void
  }

  interface Window {
    chrome?: {
      webview?: ChromeWebView
    }
  }
}
