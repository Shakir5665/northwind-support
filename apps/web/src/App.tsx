import { Link, NavLink, Outlet } from 'react-router-dom';
import { useAuth } from './features/auth/useAuth';

/** Application shell: the header, navigation, agent identity, and content outlet. */
export function App() {
  const { user, logout } = useAuth();

  return (
    <div className="app">
      <header className="app__header">
        <Link className="app__brand" to="/tickets">
          Northwind Support
        </Link>

        {user && (
          <nav className="app__nav" aria-label="Main">
            <NavLink to="/tickets">Tickets</NavLink>
            <NavLink to="/customers">Customers</NavLink>
          </nav>
        )}

        {user && (
          <div className="app__user">
            <span className="app__user-name">{user.name}</span>
            <button
              type="button"
              className="button button--ghost"
              onClick={logout}
              aria-label="Sign out"
            >
              Sign out
            </button>
          </div>
        )}
      </header>

      <main className="app__main">
        <Outlet />
      </main>
    </div>
  );
}
