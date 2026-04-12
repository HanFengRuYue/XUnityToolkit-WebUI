class ApiError extends Error {
    status;
    constructor(status, message) {
        super(message);
        this.status = status;
        this.name = 'ApiError';
    }
}
async function request(url, options) {
    const response = await fetch(url, {
        headers: { 'Content-Type': 'application/json' },
        ...options,
    });
    if (!response.ok) {
        const text = await response.text();
        let message = `HTTP ${response.status}`;
        try {
            const json = JSON.parse(text);
            if (json.error)
                message = json.error;
        }
        catch {
            message = text || message;
        }
        throw new ApiError(response.status, message);
    }
    const result = (await response.json());
    if (!result.success && result.error) {
        throw new ApiError(response.status, result.error);
    }
    return result.data;
}
export const api = {
    get: (url) => request(url),
    post: (url, body) => request(url, {
        method: 'POST',
        body: body !== undefined ? JSON.stringify(body) : undefined,
    }),
    put: (url, body) => request(url, {
        method: 'PUT',
        body: body !== undefined ? JSON.stringify(body) : undefined,
    }),
    del: (url) => request(url, { method: 'DELETE' }),
};
