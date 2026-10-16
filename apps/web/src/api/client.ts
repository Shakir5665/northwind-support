import type { CurrentUser, ProblemDetails } from '../types/api';

/**
 * Storage keys for persisting authentication state in sessionStorage.
 * sessionStorage ensures the token and profile are isolated to the browser tab
 * and automatically cleared when the tab is closed.
 */
export const TOKEN_STORAGE_KEY = 'auth_token';
export const USER_STORAGE_KEY = 'auth_user';

/**
 * Where the API lives. In development this stays '/api' and the Vite dev server proxies it
 * to the backend; set VITE_API_BASE_URL to point a build somewhere else. See .env.example.
 */
const baseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api';

/** Retrieves the stored JWT token from sessionStorage, or null if unauthenticated. */
export function getStoredToken(): string | null {
  try {
    return sessionStorage.getItem(TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

/** Retrieves the stored agent information from sessionStorage, or null if unauthenticated. */
export function getStoredUser(): CurrentUser | null {
  try {
    const raw = sessionStorage.getItem(USER_STORAGE_KEY);
    return raw ? (JSON.parse(raw) as CurrentUser) : null;
  } catch {
    return null;
  }
}

/** Persists the JWT access token and authenticated agent profile into sessionStorage. */
export function setStoredSession(token: string, user: CurrentUser): void {
  try {
    sessionStorage.setItem(TOKEN_STORAGE_KEY, token);
    sessionStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
  } catch {
    // Gracefully handle environments where storage is disabled or quota exceeded
  }
}

/** Clears the JWT access token and agent profile from sessionStorage. */
export function clearStoredSession(): void {
  try {
    sessionStorage.removeItem(TOKEN_STORAGE_KEY);
    sessionStorage.removeItem(USER_STORAGE_KEY);
  } catch {
    // Ignore storage errors during cleanup
  }
}

/**
 * Validates and sanitizes a return URL.
 * Prevents open redirects (rejects external domains or protocol-relative URLs)
 * and prevents redirect loops back to /login.
 */
export function getSafeReturnUrl(target?: string | null): string {
  if (!target) {
    return '/tickets';
  }

  // Must be an internal relative path starting with '/' and not '//' or '/\'
  if (target.startsWith('/') && !target.startsWith('//') && !target.startsWith('/\\')) {
    // Disallow redirecting directly back to /login to prevent redirect loops
    if (target === '/login' || target.startsWith('/login?') || target.startsWith('/login/')) {
      return '/tickets';
    }
    return target;
  }

  return '/tickets';
}

type UnauthorizedHandler = (currentPath?: string) => void;

let unauthorizedHandler: UnauthorizedHandler | null = null;
let isHandlingUnauthorized = false;

/** Registers a global callback to invoke when an authenticated request receives HTTP 401. */
export function setUnauthorizedHandler(handler: UnauthorizedHandler | null): void {
  unauthorizedHandler = handler;
  isHandlingUnauthorized = false;
}

/** Resets the 401 debounce guard (primarily used for test isolation). */
export function resetUnauthorizedGuard(): void {
  isHandlingUnauthorized = false;
}


/**
 * Centrally handles 401 responses for authenticated API requests.
 * Wipes the local session, prevents duplicate concurrent redirect storms,
 * and notifies listeners or redirects to /login.
 */
export function handleUnauthorized(currentPath?: string): void {
  clearStoredSession();

  if (isHandlingUnauthorized) {
    return;
  }
  isHandlingUnauthorized = true;

  // Reset guard after 1 second to debounce concurrent 401 failures
  setTimeout(() => {
    isHandlingUnauthorized = false;
  }, 1000);

  if (unauthorizedHandler) {
    unauthorizedHandler(currentPath);
  } else if (typeof window !== 'undefined' && window.location) {
    const destination = currentPath || (window.location.pathname + window.location.search);
    const safeDestination = getSafeReturnUrl(destination);
    if (window.location.pathname !== '/login') {
      window.location.assign(`/login?from=${encodeURIComponent(safeDestination)}`);
    }
  }
}

/** A failed HTTP call, carrying the problem document the API returned. */
export class ApiError extends Error {
  readonly status: number;

  readonly problem?: ProblemDetails;

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  /** Field name to messages, when the failure was a validation failure. */
  get fieldErrors(): Record<string, string[]> {
    return this.problem?.errors ?? {};
  }
}

/**
 * The single place that talks to the API. Returns parsed JSON, or throws an
 * {@link ApiError} that callers can show to the user.
 * Attaches the Bearer token to authenticated endpoints and centrally handles 401.
 */
export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = getStoredToken();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...((init?.headers as Record<string, string> | undefined) ?? {}),
  };

  // AU-10: Attach Bearer token to authenticated requests (omitting for login)
  if (token && path !== '/auth/login' && !headers['Authorization']) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers,
  });

  if (!response.ok) {
    const problem = await readProblem(response);

    // AU-13: Centrally handle 401 Unauthorized for authenticated endpoints.
    // Avoid treating a normal login failure (/auth/login) as an expired session.
    if (response.status === 401 && path !== '/auth/login') {
      const currentPath =
        typeof window !== 'undefined'
          ? window.location.pathname + window.location.search
          : undefined;
      handleUnauthorized(currentPath);
    }

    throw new ApiError(
      response.status,
      problem?.detail ?? problem?.title ?? response.statusText,
      problem,
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

/** Turns anything thrown by a fetch into a message worth showing a user. */
export function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    return error.message;
  }

  if (error instanceof Error) {
    return `${fallback} (${error.message})`;
  }

  return fallback;
}

async function readProblem(response: Response): Promise<ProblemDetails | undefined> {
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    // Not every failure has a JSON body - a dead server certainly will not.
    return undefined;
  }
}
