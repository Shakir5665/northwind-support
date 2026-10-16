import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CurrentUser, ProblemDetails } from '../types/api';
import {
  ApiError,
  clearStoredSession,
  getSafeReturnUrl,
  getStoredToken,
  getStoredUser,
  handleUnauthorized,
  request,
  resetUnauthorizedGuard,
  setStoredSession,
  setUnauthorizedHandler,
  TOKEN_STORAGE_KEY,
  USER_STORAGE_KEY,
} from './client';

describe('client.ts authentication & 401 handling', () => {
  beforeEach(() => {
    sessionStorage.clear();
    resetUnauthorizedGuard();
    setUnauthorizedHandler(null);
    vi.restoreAllMocks();
  });


  afterEach(() => {
    sessionStorage.clear();
    setUnauthorizedHandler(null);
  });

  const mockUser: CurrentUser = {
    agentId: 1,
    name: 'Alex Turner',
    email: 'alex.turner@northwind-support.example',
  };

  describe('Storage helpers', () => {
    it('sets, retrieves, and clears stored token and user profile in sessionStorage', () => {
      expect(getStoredToken()).toBeNull();
      expect(getStoredUser()).toBeNull();

      setStoredSession('test-jwt-token', mockUser);

      expect(getStoredToken()).toBe('test-jwt-token');
      expect(getStoredUser()).toEqual(mockUser);
      expect(sessionStorage.getItem(TOKEN_STORAGE_KEY)).toBe('test-jwt-token');
      expect(JSON.parse(sessionStorage.getItem(USER_STORAGE_KEY)!)).toEqual(mockUser);

      clearStoredSession();

      expect(getStoredToken()).toBeNull();
      expect(getStoredUser()).toBeNull();
      expect(sessionStorage.getItem(TOKEN_STORAGE_KEY)).toBeNull();
      expect(sessionStorage.getItem(USER_STORAGE_KEY)).toBeNull();
    });
  });

  describe('getSafeReturnUrl', () => {
    it('returns /tickets for undefined, empty, or null target', () => {
      expect(getSafeReturnUrl(undefined)).toBe('/tickets');
      expect(getSafeReturnUrl(null)).toBe('/tickets');
      expect(getSafeReturnUrl('')).toBe('/tickets');
    });

    it('returns internal relative path with query parameters intact', () => {
      expect(getSafeReturnUrl('/tickets/15?tab=escalations')).toBe('/tickets/15?tab=escalations');
      expect(getSafeReturnUrl('/customers?tier=Premium')).toBe('/customers?tier=Premium');
    });

    it('rejects external or protocol-relative URLs and falls back to /tickets', () => {
      expect(getSafeReturnUrl('https://evil.com')).toBe('/tickets');
      expect(getSafeReturnUrl('//evil.com/phish')).toBe('/tickets');
      expect(getSafeReturnUrl('/\\evil.com')).toBe('/tickets');
      expect(getSafeReturnUrl('javascript:alert(1)')).toBe('/tickets');
    });

    it('rejects /login to prevent redirect loops and falls back to /tickets', () => {
      expect(getSafeReturnUrl('/login')).toBe('/tickets');
      expect(getSafeReturnUrl('/login?from=/tickets')).toBe('/tickets');
      expect(getSafeReturnUrl('/login/reset')).toBe('/tickets');
    });
  });

  describe('request() token attachment (AU-10)', () => {
    it('attaches Authorization: Bearer <token> to authenticated API requests when token is stored', async () => {
      setStoredSession('valid-bearer-token', mockUser);

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        new Response(JSON.stringify({ success: true }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );

      await request('/tickets');

      expect(fetchSpy).toHaveBeenCalledTimes(1);
      const callArgs = fetchSpy.mock.calls[0];
      const headers = callArgs[1]?.headers as Record<string, string>;

      expect(headers['Authorization']).toBe('Bearer valid-bearer-token');
    });

    it('does not attach Authorization header if no token is stored in sessionStorage', async () => {
      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        new Response(JSON.stringify({ success: true }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );

      await request('/tickets');

      expect(fetchSpy).toHaveBeenCalledTimes(1);
      const callArgs = fetchSpy.mock.calls[0];
      const headers = callArgs[1]?.headers as Record<string, string>;

      expect(headers['Authorization']).toBeUndefined();
    });

    it('does not attach Authorization header to /auth/login even if token exists', async () => {
      setStoredSession('stale-token', mockUser);

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        new Response(JSON.stringify({ token: 'new-token' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );

      await request('/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email: 'test@example.com', password: 'pass' }),
      });

      expect(fetchSpy).toHaveBeenCalledTimes(1);
      const callArgs = fetchSpy.mock.calls[0];
      const headers = callArgs[1]?.headers as Record<string, string>;

      expect(headers['Authorization']).toBeUndefined();
    });
  });

  describe('request() HTTP 401 handling (AU-13)', () => {
    it('clears stored session and calls unauthorized handler on 401 from authenticated request', async () => {
      setStoredSession('expired-token', mockUser);
      const unauthorizedHandler = vi.fn();
      setUnauthorizedHandler(unauthorizedHandler);

      vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        new Response(JSON.stringify({ detail: 'Token has expired.' } as ProblemDetails), {
          status: 401,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
      );

      await expect(request('/tickets')).rejects.toThrow(ApiError);

      expect(getStoredToken()).toBeNull();
      expect(getStoredUser()).toBeNull();
      expect(unauthorizedHandler).toHaveBeenCalledTimes(1);
    });

    it('does NOT trigger unauthorized handler or session reset when /auth/login returns 401', async () => {
      const unauthorizedHandler = vi.fn();
      setUnauthorizedHandler(unauthorizedHandler);

      vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        new Response(JSON.stringify({ detail: 'Invalid email or password.' } as ProblemDetails), {
          status: 401,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
      );

      await expect(
        request('/auth/login', {
          method: 'POST',
          body: JSON.stringify({ email: 'bad@example.com', password: 'wrong' }),
        }),
      ).rejects.toThrow('Invalid email or password.');

      expect(unauthorizedHandler).not.toHaveBeenCalled();
    });

    it('debounces concurrent 401 responses so unauthorized handler is not called repeatedly', () => {
      const unauthorizedHandler = vi.fn();
      setUnauthorizedHandler(unauthorizedHandler);

      handleUnauthorized('/tickets');
      handleUnauthorized('/tickets');
      handleUnauthorized('/agents');

      expect(unauthorizedHandler).toHaveBeenCalledTimes(1);
    });
  });
});
