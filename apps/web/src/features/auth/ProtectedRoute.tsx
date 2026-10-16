import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from './useAuth';

/**
 * Route guard that requires an authenticated session.
 * Unauthenticated visitors are redirected to /login, preserving their original
 * path and query parameters in navigation state for post-login redirection.
 */
export function ProtectedRoute() {
  const { isAuthenticated } = useAuth();
  const location = useLocation();

  if (!isAuthenticated) {
    const destination = `${location.pathname}${location.search}`;
    return <Navigate to="/login" state={{ from: destination }} replace />;
  }

  return <Outlet />;
}
