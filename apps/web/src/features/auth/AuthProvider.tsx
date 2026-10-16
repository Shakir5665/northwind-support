import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import {
  clearStoredSession,
  getSafeReturnUrl,
  getStoredToken,
  getStoredUser,
  setStoredSession,
  setUnauthorizedHandler,
} from '../../api/client';
import type { CurrentUser } from '../../types/api';
import { AuthContext, type AuthContextValue } from './AuthContext';

interface AuthProviderProps {
  children: React.ReactNode;
}

export function AuthProvider({ children }: AuthProviderProps) {
  const [token, setToken] = useState<string | null>(() => getStoredToken());
  const [user, setUser] = useState<CurrentUser | null>(() => getStoredUser());
  const navigate = useNavigate();
  const location = useLocation();

  // AU-13: Register global 401 callback to reset state and navigate to /login
  useEffect(() => {
    setUnauthorizedHandler((currentPath) => {
      setToken(null);
      setUser(null);
      const destination = currentPath || `${location.pathname}${location.search}`;
      const safeDestination = getSafeReturnUrl(destination);
      navigate('/login', { state: { from: safeDestination }, replace: true });
    });

    return () => {
      setUnauthorizedHandler(null);
    };
  }, [navigate, location]);

  const login = useCallback((newToken: string, newUser: CurrentUser) => {
    setStoredSession(newToken, newUser);
    setToken(newToken);
    setUser(newUser);
  }, []);

  const logout = useCallback(() => {
    clearStoredSession();
    setToken(null);
    setUser(null);
    navigate('/login', { replace: true });
  }, [navigate]);

  const value = useMemo<AuthContextValue>(
    () => ({
      token,
      user,
      isAuthenticated: Boolean(token && user),
      login,
      logout,
    }),
    [token, user, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
