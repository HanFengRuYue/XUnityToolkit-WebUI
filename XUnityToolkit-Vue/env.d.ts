/// <reference types="vite/client" />

import 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    depth?: number
  }
}

// Read-only WinUI shell descriptor and the single structured web-to-host message channel.
declare global {
  interface XUnityDesktopHostDescriptor {
    readonly protocolVersion: 1
    readonly shell: 'winui3'
    readonly nativeTitleBar: true
  }

  interface ChromeWebView {
    postMessage(message: unknown): void
  }

  interface Window {
    readonly __XUNITY_DESKTOP_HOST__?: XUnityDesktopHostDescriptor
    chrome?: {
      webview?: ChromeWebView
    }
  }
}
