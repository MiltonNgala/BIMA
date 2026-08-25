export type ApiClient = {
  get<T>(path: string): Promise<T>
  post<T>(path: string, body: unknown): Promise<T>
  patch<T>(path: string, body: unknown): Promise<T>
  delete(path: string): Promise<void>
}

export function createApiClient(accessToken: string): ApiClient {
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5180'
  const request = async <T>(path: string, init: RequestInit = {}) => {
    const response = await fetch(`${baseUrl}${path}`, {
      ...init,
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${accessToken}`, ...init.headers },
    })
    if (!response.ok) throw new Error(`API request failed with status ${response.status}.`)
    if (response.status === 204) return undefined as T
    return response.json() as Promise<T>
  }
  return {
    get: <T>(path: string) => request<T>(path),
    post: <T>(path: string, body: unknown) => request<T>(path, { method: 'POST', body: JSON.stringify(body) }),
    patch: <T>(path: string, body: unknown) => request<T>(path, { method: 'PATCH', body: JSON.stringify(body) }),
    delete: (path: string) => request<void>(path, { method: 'DELETE' }),
  }
}
