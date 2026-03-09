import type { ApiResult } from './types'

class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })

  if (!response.ok) {
    const text = await response.text()
    let message = `HTTP ${response.status}`
    try {
      const json = JSON.parse(text) as ApiResult<unknown>
      if (json.error) message = json.error
    } catch {
      message = text || message
    }
    throw new ApiError(response.status, message)
  }

  const result = (await response.json()) as ApiResult<T>
  if (!result.success && result.error) {
    throw new ApiError(response.status, result.error)
  }
  return result.data as T
}

export const api = {
  get: <T>(url: string) => request<T>(url),

  post: <T>(url: string, body?: unknown) =>
    request<T>(url, {
      method: 'POST',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    }),

  put: <T>(url: string, body?: unknown) =>
    request<T>(url, {
      method: 'PUT',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    }),

  del: <T>(url: string) => request<T>(url, { method: 'DELETE' }),
}
