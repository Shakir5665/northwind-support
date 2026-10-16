import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from '../../api/auth';
import { ApiError } from '../../api/client';
import type { CurrentUser, LoginResponse } from '../../types/api';
import { AuthProvider } from './AuthProvider';
import { LoginPage } from './LoginPage';

vi.mock('../../api/auth', () => ({
  authApi: {
    login: vi.fn(),
    getCurrentUser: vi.fn(),
  },
}));

const mockLogin = vi.mocked(authApi.login);

describe('LoginPage', () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.clearAllMocks();
  });

  const renderLoginPage = (
    initialEntries = ['/login'],
    initialSession?: { token: string; user: CurrentUser },
  ) => {

    if (initialSession) {
      sessionStorage.setItem('auth_token', initialSession.token);
      sessionStorage.setItem('auth_user', JSON.stringify(initialSession.user));
    }

    return render(
      <MemoryRouter initialEntries={initialEntries}>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/tickets" element={<div>Tickets Page Content</div>} />
            <Route path="/tickets/42" element={<div>Ticket 42 Detail Content</div>} />
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    );
  };

  it('renders login form with labelled email and password fields and submit button', () => {
    renderLoginPage();

    expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/password/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
  });

  it('prevents submission and displays validation errors when required fields are missing', async () => {
    renderLoginPage();

    const submitButton = screen.getByRole('button', { name: /sign in/i });
    fireEvent.click(submitButton);

    expect(await screen.findByText('Email is required.')).toBeInTheDocument();
    expect(await screen.findByText('Password is required.')).toBeInTheDocument();
    expect(mockLogin).not.toHaveBeenCalled();
  });

  it('validates email format and displays error for invalid email', async () => {
    renderLoginPage();

    const emailInput = screen.getByLabelText(/email/i);
    const passwordInput = screen.getByLabelText(/password/i);

    fireEvent.change(emailInput, { target: { value: 'not-an-email' } });
    fireEvent.change(passwordInput, { target: { value: 'password123' } });

    fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByText('Please enter a valid email address.')).toBeInTheDocument();
    expect(mockLogin).not.toHaveBeenCalled();
  });

  it('displays a clear error and does not create an authenticated session when credentials are rejected (401)', async () => {
    mockLogin.mockRejectedValue(new ApiError(401, 'Invalid email or password.'));

    renderLoginPage();

    const emailInput = screen.getByLabelText(/email/i);
    const passwordInput = screen.getByLabelText(/password/i);

    fireEvent.change(emailInput, { target: { value: 'bad@example.com' } });
    fireEvent.change(passwordInput, { target: { value: 'wrongpassword' } });

    fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByText('Invalid email or password.')).toBeInTheDocument();
    expect(sessionStorage.getItem('auth_token')).toBeNull();
    expect(sessionStorage.getItem('auth_user')).toBeNull();
    expect(screen.queryByText('Tickets Page Content')).not.toBeInTheDocument();
  });

  it('displays a helpful message when network or unexpected errors occur', async () => {
    mockLogin.mockRejectedValue(new Error('Failed to fetch'));

    renderLoginPage();

    const emailInput = screen.getByLabelText(/email/i);
    const passwordInput = screen.getByLabelText(/password/i);

    fireEvent.change(emailInput, { target: { value: 'agent@example.com' } });
    fireEvent.change(passwordInput, { target: { value: 'DevPassword123!' } });

    fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

    expect(
      await screen.findByText(/Unable to sign in\. Please verify your connection/i),
    ).toBeInTheDocument();
    expect(sessionStorage.getItem('auth_token')).toBeNull();
  });

  it('shows pending state and disables controls while login request is in progress', async () => {
    let resolveLogin!: (res: LoginResponse) => void;
    mockLogin.mockReturnValue(
      new Promise((resolve) => {
        resolveLogin = resolve;
      }),
    );

    renderLoginPage();

    const emailInput = screen.getByLabelText(/email/i);
    const passwordInput = screen.getByLabelText(/password/i);
    const submitButton = screen.getByRole('button', { name: /sign in/i });

    fireEvent.change(emailInput, { target: { value: 'agent@example.com' } });
    fireEvent.change(passwordInput, { target: { value: 'DevPassword123!' } });

    fireEvent.click(submitButton);

    expect(screen.getByRole('button', { name: /signing in\.\.\./i })).toBeDisabled();
    expect(emailInput).toBeDisabled();
    expect(passwordInput).toBeDisabled();

    // Resolve the promise
    resolveLogin({
      token: 'jwt-token-123',
      expiresAtUtc: '2026-10-17T12:00:00Z',
      agentId: 1,
      name: 'Alex Turner',
      email: 'agent@example.com',
    });

    await waitFor(() => {
      expect(screen.getByText('Tickets Page Content')).toBeInTheDocument();
    });
  });

  it('stores token and user profile and navigates to default destination (/tickets) on success', async () => {
    const successResponse: LoginResponse = {
      token: 'valid-jwt-token',
      expiresAtUtc: '2026-10-17T12:00:00Z',
      agentId: 1,
      name: 'Alex Turner',
      email: 'alex.turner@northwind-support.example',
    };
    mockLogin.mockResolvedValue(successResponse);

    renderLoginPage();

    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: 'alex.turner@northwind-support.example' },
    });
    fireEvent.change(screen.getByLabelText(/password/i), {
      target: { value: 'DevPassword123!' },
    });

    fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => {
      expect(screen.getByText('Tickets Page Content')).toBeInTheDocument();
    });

    expect(sessionStorage.getItem('auth_token')).toBe('valid-jwt-token');
    const storedUser = JSON.parse(sessionStorage.getItem('auth_user')!);
    expect(storedUser).toEqual({
      agentId: 1,
      name: 'Alex Turner',
      email: 'alex.turner@northwind-support.example',
    });
  });

  it('navigates to intended return destination preserved in navigation state', async () => {
    const successResponse: LoginResponse = {
      token: 'valid-jwt-token',
      expiresAtUtc: '2026-10-17T12:00:00Z',
      agentId: 1,
      name: 'Alex Turner',
      email: 'alex.turner@northwind-support.example',
    };
    mockLogin.mockResolvedValue(successResponse);

    render(
      <MemoryRouter initialEntries={[{ pathname: '/login', state: { from: '/tickets/42' } }]}>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/tickets" element={<div>Tickets Page Content</div>} />
            <Route path="/tickets/42" element={<div>Ticket 42 Detail Content</div>} />
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: 'alex.turner@northwind-support.example' },
    });
    fireEvent.change(screen.getByLabelText(/password/i), {
      target: { value: 'DevPassword123!' },
    });

    fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => {
      expect(screen.getByText('Ticket 42 Detail Content')).toBeInTheDocument();
    });
  });

  it('redirects already-authenticated users away from /login to tickets page', async () => {
    renderLoginPage(['/login'], {
      token: 'existing-token',
      user: { agentId: 1, name: 'Alex Turner', email: 'alex@example.com' },
    });

    await waitFor(() => {
      expect(screen.getByText('Tickets Page Content')).toBeInTheDocument();
    });
  });
});
