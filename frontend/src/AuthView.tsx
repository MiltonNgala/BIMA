import type { FormEvent } from 'react'

type AuthMode = 'login' | 'register' | 'forgot' | 'reset'

type Props = {
  mode: AuthMode
  error: string
  message: string
  resetToken: string
  onModeChange: (mode: AuthMode) => void
  onLogin: (event: FormEvent<HTMLFormElement>) => void
  onRegister: (event: FormEvent<HTMLFormElement>) => void
  onForgot: (event: FormEvent<HTMLFormElement>) => void
  onReset: (event: FormEvent<HTMLFormElement>) => void
}

export function AuthView({ mode, error, message, resetToken, onModeChange, onLogin, onRegister, onForgot, onReset }: Props) {
  const isLogin = mode === 'login'
  const title = isLogin ? 'Welcome back' : mode === 'register' ? 'Create your workspace' : mode === 'forgot' ? 'Recover access' : 'Choose a new password'
  const submit = mode === 'login' ? onLogin : mode === 'register' ? onRegister : mode === 'forgot' ? onForgot : onReset

  return <main className="login-shell"><div className="login-art"><span className="brand-mark">B</span><p className="eyebrow">CORE INSURANCE OPERATIONS</p><h1>Clarity for every policy.</h1><p>Manage your portfolio, claims, customers, and billing from one calm workspace.</p></div><form className="login-form" onSubmit={submit}><div className="brand dark"><span className="brand-mark">B</span><span>BIMA</span></div><p className="eyebrow">{isLogin ? 'SECURE SIGN IN' : 'ACCOUNT ACCESS'}</p><h2>{title}</h2>{mode === 'register' && <label>Display name<input name="displayName" autoComplete="name" required maxLength={200} /></label>}{mode !== 'reset' && <><label>Tenant<input name="tenantId" defaultValue="demo" required maxLength={64} /></label><label>Email<input name="email" type="email" autoComplete="email" defaultValue={isLogin ? 'operator@bima.local' : ''} required maxLength={200} /></label></>}{mode === 'reset' && <label>Reset token<input name="token" defaultValue={resetToken} required /></label>}{(mode === 'login' || mode === 'register' || mode === 'reset') && <label>{mode === 'reset' ? 'New password' : 'Password'}<input name={mode === 'reset' ? 'newPassword' : 'password'} type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} minLength={12} required /></label>}{error && <p className="form-error" role="alert">{error}</p>}{message && <p className="data-note" role="status">{message}</p>}<button className="primary-button" type="submit">{isLogin ? 'Sign in' : mode === 'register' ? 'Create account' : mode === 'forgot' ? 'Send recovery instructions' : 'Reset password'} <span>-&gt;</span></button><div className="auth-links">{isLogin && <><button type="button" onClick={() => onModeChange('forgot')}>Forgot password?</button><button type="button" onClick={() => onModeChange('register')}>Create an account</button></>}{mode !== 'login' && <button type="button" onClick={() => onModeChange('login')}>Back to sign in</button>}{mode === 'forgot' && <button type="button" onClick={() => onModeChange('reset')}>I have a reset token</button>}</div>{mode === 'login' && <small className="login-help">Use your organization account to continue.</small>}</form></main>
}
