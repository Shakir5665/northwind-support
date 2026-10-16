import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, resetUnauthorizedGuard } from '../../api/client';
import { App } from '../../App';
import type { CurrentUser } from '../../types/api';
import { AuthProvider } from './AuthProvider';
import { useAuth } from './useAuth';
import { LoginPage } from './LoginPage';
import { ProtectedRoute } from './ProtectedRoute';

describe('AuthFlow & Protected Routes (AU-11, AU-12, AU-13)', () => {
  beforeEach(() => {
    sessionStorage.clear();
    resetUnauthorizedGuard();
    vi.restoreAllMocks();
  });

  afterEach(() => {
    sessionStorage.clear();
    resetUnauthorizedGuard();
  });

  const mockAgent: CurrentUser = {
    agentId: 1,
    name: 'Alex Turner',
    email: 'alex.turner@northwind-support.example',
  };

  function ProtectedSamplePage() {
    const { logout } = useAuth();
    return (
      <div>
        <h2>Protected Tickets Workspace</h2>
        <button type="button" onClick={logout}>
          Custom Logout
        </button>
      </div>
    );
  }

  function renderAppWithRouter(
    initialEntries = ['/tickets'],
    initialSession?: { token: string; user: CurrentUser },
  ) {
    if (initialSession) {
      sessionStorage.setItem('auth_token', initialSession.token);
      sessionStorage.setItem('auth_user', JSON.stringify(initialSession.user));
    }

    return render(
      <MemoryRouter initialEntries={initialEntries}>
        <AuthProvider>
          <Routes>
            <Route path="/" element={<App />}>
              <Route path="login" element={<LoginPage />} />
              <Route element={<ProtectedRoute />}>
                <Route path="tickets" element={<ProtectedSamplePage />} />
                <Route path="customers" element={<div>Protected Customers Workspace</div>} />
              </Route>
            </Route>
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    );
  }

  it('redirects unauthenticated visitors to /login when accessing protected routes', async () => {
    renderAppWithRouter(['/tickets']);

    expect(await screen.findByRole('heading', { name: /sign in/i })).toBeInTheDocument();
    expect(screen.queryByText('Protected Tickets Workspace')).not.toBeInTheDocument();
  });

  it('renders protected page and displays signed-in agent name in application header when authenticated', async () => {
    renderAppWithRouter(['/tickets'], {
      token: 'valid-jwt',
      user: mockAgent,
    });

    expect(await screen.findByText('Protected Tickets Workspace')).toBeInTheDocument();
    expect(screen.getByText('Alex Turner')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /sign out/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /tickets/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /customers/i })).toBeInTheDocument();
  });

  it('signs out the user, clears session, updates UI, and redirects to /login on logout action', async () => {
    renderAppWithRouter(['/tickets'], {
      token: 'valid-jwt',
      user: mockAgent,
    });

    expect(await screen.findByText('Protected Tickets Workspace')).toBeInTheDocument();
    expect(screen.getByText('Alex Turner')).toBeInTheDocument();

    const signOutButton = screen.getByRole('button', { name: /sign out/i });
    fireEvent.click(signOutButton);

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument();
    });

    expect(screen.queryByText('Alex Turner')).not.toBeInTheDocument();
    expect(screen.queryByText('Protected Tickets Workspace')).not.toBeInTheDocument();
    expect(sessionStorage.getItem('auth_token')).toBeNull();
    expect(sessionStorage.getItem('auth_user')).toBeNull();
  });

  it('clears session and redirects to /login when a 401 is received from an authenticated request', async () => {
    renderAppWithRouter(['/tickets'], {
      token: 'expired-jwt',
      user: mockAgent,
    });

    expect(await screen.findByText('Protected Tickets Workspace')).toBeInTheDocument();
    expect(screen.getByText('Alex Turner')).toBeInTheDocument();

    // Simulate an authenticated API call returning HTTP 401
    const { request } = await import('../../api/client');
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ detail: 'Token expired' }), {
        status: 401,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    );

    await act(async () => {
      await expect(request('/tickets/15')).rejects.toThrow(ApiError);
    });


    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument();
    });

    expect(sessionStorage.getItem('auth_token')).toBeNull();
    expect(sessionStorage.getItem('auth_user')).toBeNull();
    expect(screen.queryByText('Alex Turner')).not.toBeInTheDocument();
  });
});
