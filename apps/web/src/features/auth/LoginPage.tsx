import React, { useEffect, useState } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { getSafeReturnUrl, toErrorMessage } from '../../api/client';
import { ErrorMessage } from '../../components/ErrorMessage';
import { Field } from '../../components/Field';
import { useAuth } from './useAuth';

const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

interface FormErrors {
  email?: string;
  password?: string;
}

export function LoginPage() {
  const { isAuthenticated, login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitError, setSubmitError] = useState<string>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Derive safe return destination from navigation state or query parameter
  const fromState = (location.state as { from?: string } | undefined)?.from;
  const fromQuery = searchParams.get('from');
  const returnUrl = getSafeReturnUrl(fromState || fromQuery);

  // If already authenticated, redirect away to destination or ticket list
  useEffect(() => {
    if (isAuthenticated) {
      navigate(returnUrl, { replace: true });
    }
  }, [isAuthenticated, navigate, returnUrl]);

  const validate = (): boolean => {
    const nextErrors: FormErrors = {};

    const trimmedEmail = email.trim();
    if (!trimmedEmail) {
      nextErrors.email = 'Email is required.';
    } else if (!EMAIL_REGEX.test(trimmedEmail)) {
      nextErrors.email = 'Please enter a valid email address.';
    }

    if (!password) {
      nextErrors.password = 'Password is required.';
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSubmitError(undefined);

    if (isSubmitting) {
      return;
    }

    if (!validate()) {
      return;
    }

    setIsSubmitting(true);

    try {
      const response = await authApi.login({
        email: email.trim(),
        password,
      });

      login(response.token, {
        agentId: response.agentId,
        name: response.name,
        email: response.email,
      });

      navigate(returnUrl, { replace: true });
    } catch (error) {
      setSubmitError(
        toErrorMessage(
          error,
          'Unable to sign in. Please verify your connection and try again.',
        ),
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="login-container">
      <div className="card login-card">
        <header className="login-card__header">
          <h1>Sign in</h1>
          <p className="login-card__subtitle">Northwind Support Desk</p>
        </header>

        {submitError && (
          <div style={{ marginBottom: '16px' }}>
            <ErrorMessage message={submitError} />
          </div>
        )}

        <form onSubmit={handleSubmit} noValidate>
          <Field id="login-email" label="Email" error={errors.email}>
            {(props) => (
              <input
                {...props}
                type="email"
                autoComplete="email"
                value={email}
                disabled={isSubmitting}
                placeholder="agent@northwind.local"
                onChange={(e) => {
                  setEmail(e.target.value);
                  if (errors.email) {
                    setErrors((prev) => ({ ...prev, email: undefined }));
                  }
                }}
              />
            )}
          </Field>

          <Field id="login-password" label="Password" error={errors.password}>
            {(props) => (
              <input
                {...props}
                type="password"
                autoComplete="current-password"
                value={password}
                disabled={isSubmitting}
                onChange={(e) => {
                  setPassword(e.target.value);
                  if (errors.password) {
                    setErrors((prev) => ({ ...prev, password: undefined }));
                  }
                }}
              />
            )}
          </Field>

          <div style={{ marginTop: '20px' }}>
            <button
              type="submit"
              className="button button--primary"
              disabled={isSubmitting}
              style={{ width: '100%', justifyContent: 'center' }}
            >
              {isSubmitting ? 'Signing in...' : 'Sign in'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
