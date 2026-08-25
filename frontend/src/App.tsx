import { startTransition, useEffect, useEffectEvent, useState } from 'react'
import type { ChangeEvent, FormEvent } from 'react'
import './App.css'
import { WorkspaceView } from './WorkspaceView'
import { AuthView } from './AuthView'

type Policy = { number: string; customer: string; product: string; status: string; premium: number; renewalDate: string }
type Claim = { claimNumber: string; policyNumber: string; customer: string; description: string; status: string; reserveAmount: number; paidAmount: number; lossDate: string }
type Customer = { id: string; name: string; email: string; phone: string; customerType: string }
type Invoice = { invoiceNumber: string; policyNumber: string; amount: number; paidAmount: number; dueDate: string; status: string }
type Payment = { id: string; reference: string; amount: number; receivedAt: string }
type Attachment = { id: string; fileName: string; contentType: string; sizeBytes: number; uploadedAt: string }
type Session = { id: string; createdAt: string; expiresAt: string; revokedAt: string | null }
type AuditEvent = { id: string; userId: string; action: string; entityType: string; entityId: string | null; createdAt: string; metadata: string | null }
type Organization = { tenantId: string; name: string; isActive: boolean }
type AuthResponse = { accessToken: string; refreshToken: string; userId: string; tenantId: string; role: string; expiresIn: number }
type User = { id: string; email: string; displayName: string; role: string; isActive: boolean }
type AuthMode = 'login' | 'register' | 'forgot' | 'reset'

const apiUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5180'
const money = (value: number) => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }).format(value)
const dateTime = (value: string) => new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
const fileSize = (value: number) => `${(value / 1024 / 1024).toFixed(1)} MB`
let refreshInFlight: Promise<AuthResponse> | null = null

function App() {
  const [auth, setAuth] = useState<AuthResponse | null>(null)
  const [storedSession] = useState<AuthResponse | null>(() => {
    const serialized = localStorage.getItem('bima.session')
    if (!serialized) return null
    try { return JSON.parse(serialized) as AuthResponse } catch { localStorage.removeItem('bima.session'); localStorage.removeItem('bima.refreshToken'); return null }
  })
  const [loginError, setLoginError] = useState('')
  const [authMode, setAuthMode] = useState<AuthMode>('login')
  const [authMessage, setAuthMessage] = useState('')
  const [resetToken, setResetToken] = useState('')
  const [activeSection, setActiveSection] = useState('Overview')
  const [policies, setPolicies] = useState<Policy[]>([])
  const [claims, setClaims] = useState<Claim[]>([])
  const [customers, setCustomers] = useState<Customer[]>([])
  const [invoices, setInvoices] = useState<Invoice[]>([])
  const [payments, setPayments] = useState<Payment[]>([])
  const [sessions, setSessions] = useState<Session[]>([])
  const [auditEvents, setAuditEvents] = useState<AuditEvent[]>([])
  const [organization, setOrganization] = useState<Organization | null>(null)
  const [permissionsByUser, setPermissionsByUser] = useState<Record<string, string[]>>({})
  const [users, setUsers] = useState<User[]>([])
  const [selectedClaim, setSelectedClaim] = useState<Claim | null>(null)
  const [selectedInvoice, setSelectedInvoice] = useState<Invoice | null>(null)
  const [attachments, setAttachments] = useState<Attachment[]>([])
  const [showPolicyForm, setShowPolicyForm] = useState(false)
  const [showUserForm, setShowUserForm] = useState(false)
  const [showCustomerForm, setShowCustomerForm] = useState(false)
  const [showInvoiceForm, setShowInvoiceForm] = useState(false)
  const [showPaymentForm, setShowPaymentForm] = useState(false)
  const [notice, setNotice] = useState('')

  const saveSession = (session: AuthResponse) => {
    setAuth(session)
    localStorage.setItem('bima.session', JSON.stringify(session))
    localStorage.setItem('bima.refreshToken', session.refreshToken)
  }
  const clearSession = () => { localStorage.removeItem('bima.session'); localStorage.removeItem('bima.refreshToken'); setAuth(null) }
  const refreshSession = async (session: AuthResponse) => {
    if (refreshInFlight) return refreshInFlight
    refreshInFlight = (async () => {
    const response = await fetch(`${apiUrl}/api/auth/refresh`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: session.refreshToken }) })
    if (!response.ok) throw new Error('Your session has expired. Please sign in again.')
    const refreshed = await response.json() as AuthResponse
    saveSession(refreshed)
    return refreshed
    })()
    try { return await refreshInFlight } finally { refreshInFlight = null }
  }
  const authorizedFetch = async (session: AuthResponse, path: string, init: RequestInit = {}, canRefresh = true): Promise<Response> => {
    const contentHeaders: Record<string, string> = init.body instanceof FormData ? {} : { 'Content-Type': 'application/json' }
    const response = await fetch(`${apiUrl}${path}`, { ...init, headers: { ...contentHeaders, Authorization: `Bearer ${session.accessToken}`, ...init.headers } })
    if (response.status === 401 && canRefresh) return authorizedFetch(await refreshSession(session), path, init, false)
    return response
  }
  const request = async <T,>(path: string, init: RequestInit = {}) => {
    if (!auth) throw new Error('You are not signed in.')
    const response = await authorizedFetch(auth, path, init)
    if (!response.ok) throw new Error(response.status === 403 ? 'You do not have permission for this action.' : `Request failed with status ${response.status}.`)
    return response.status === 204 ? undefined as T : response.json() as Promise<T>
  }

  const loadWorkspace = async (session: AuthResponse) => {
    const responses = await Promise.all(['/api/policies', '/api/claims', '/api/customers', '/api/billing/invoices', '/api/auth/sessions'].map((path) => authorizedFetch(session, path)))
    if (responses.some((response) => !response.ok)) throw new Error('Unable to load the workspace.')
    const [policyResponse, claimResponse, customerResponse, invoiceResponse, sessionResponse] = responses
    setPolicies(await policyResponse.json() as Policy[]); setClaims(await claimResponse.json() as Claim[]); setCustomers(await customerResponse.json() as Customer[]); setInvoices(await invoiceResponse.json() as Invoice[]); setSessions(await sessionResponse.json() as Session[])
    const organizationResponse = await authorizedFetch(session, '/api/organization')
    if (!organizationResponse.ok) throw new Error('Unable to load the workspace.')
    setOrganization(await organizationResponse.json() as Organization)
    if (session.role === 'admin') {
      const userResponse = await authorizedFetch(session, '/api/users')
      if (!userResponse.ok) throw new Error('Unable to load the workspace.')
      const loadedUsers = await userResponse.json() as User[]
      setUsers(loadedUsers)
      const permissionEntries = await Promise.all(loadedUsers.map(async (user) => [user.id, (await (await authorizedFetch(session, `/api/users/${user.id}/permissions`)).json() as { permission: string }[]).map((item) => item.permission)] as const))
      setPermissionsByUser(Object.fromEntries(permissionEntries))
      const auditResponse = await authorizedFetch(session, '/api/audit')
      if (!auditResponse.ok) throw new Error('Unable to load the workspace.')
      setAuditEvents(await auditResponse.json() as AuditEvent[])
    }
    saveSession(session)
  }
  const login = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setLoginError(''); const data = new FormData(event.currentTarget)
    try { const response = await fetch(`${apiUrl}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ tenantId: data.get('tenantId'), email: data.get('email'), password: data.get('password') }) }); if (!response.ok) throw new Error('Invalid tenant, email, or password.'); await loadWorkspace(await response.json() as AuthResponse) } catch (error) { setLoginError(error instanceof Error ? error.message : 'Unable to sign in.') }
  }
  const changeAuthMode = (mode: AuthMode) => { setAuthMode(mode); setLoginError(''); setAuthMessage('') }
  const submitAuth = async (path: string, event: FormEvent<HTMLFormElement>, successMessage: string, onSuccess?: (data: Record<string, unknown>) => void) => {
    event.preventDefault(); setLoginError(''); setAuthMessage('')
    const data = new FormData(event.currentTarget)
    try {
      const payload = Object.fromEntries(data.entries())
      const response = await fetch(`${apiUrl}${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
      const responseData = await response.json().catch(() => ({})) as Record<string, unknown>
      if (!response.ok) throw new Error(typeof responseData.error === 'string' ? responseData.error : 'The request could not be completed.')
      onSuccess?.(responseData); setAuthMessage(successMessage)
    } catch (error) { setLoginError(error instanceof Error ? error.message : 'The request could not be completed.') }
  }
  const register = (event: FormEvent<HTMLFormElement>) => submitAuth('/api/auth/register', event, 'Account created. You can now sign in.')
  const forgot = (event: FormEvent<HTMLFormElement>) => submitAuth('/api/auth/password-reset/request', event, 'If the account exists, recovery instructions have been issued.', (data) => { if (typeof data.developmentToken === 'string') { setResetToken(data.developmentToken); setAuthMessage('Development reset token generated. Continue to reset your password.') } })
  const reset = async (event: FormEvent<HTMLFormElement>) => { await submitAuth('/api/auth/password-reset/confirm', event, 'Password reset. You can now sign in.', () => setAuthMode('login')) }
  const restoreWorkspace = useEffectEvent((session: AuthResponse) => { loadWorkspace(session).catch((error) => startTransition(() => { clearSession(); setLoginError(error instanceof Error ? error.message : 'Unable to restore your session.') })) })
  useEffect(() => { if (storedSession) restoreWorkspace(storedSession) }, [storedSession])

  const createPolicy = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); try { const policy = await request<Policy>('/api/policies', { method: 'POST', body: JSON.stringify({ number: data.get('number'), customer: data.get('customer'), product: data.get('product'), premium: Number(data.get('premium')), renewalDate: data.get('renewalDate') }) }); setPolicies((current) => [...current, policy].sort((a, b) => a.renewalDate.localeCompare(b.renewalDate))); setShowPolicyForm(false); setNotice('Policy created successfully.') } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to create policy.') } }
  const createCustomer = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); try { const customer = await request<Customer>('/api/customers', { method: 'POST', body: JSON.stringify({ name: data.get('name'), email: data.get('email'), phone: data.get('phone'), customerType: data.get('customerType') }) }); setCustomers((current) => [...current, customer].sort((a, b) => a.name.localeCompare(b.name))); setShowCustomerForm(false); setNotice('Customer created successfully.') } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to create customer.') } }
  const createInvoice = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); try { const invoice = await request<Invoice>('/api/billing/invoices', { method: 'POST', body: JSON.stringify({ invoiceNumber: data.get('invoiceNumber'), policyNumber: data.get('policyNumber'), amount: Number(data.get('amount')), dueDate: data.get('dueDate') }) }); setInvoices((current) => [...current, invoice].sort((a, b) => a.dueDate.localeCompare(b.dueDate))); setShowInvoiceForm(false); setNotice('Invoice created successfully.') } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to create invoice.') } }
  const openInvoice = async (invoiceNumber: string) => { const invoice = invoices.find((item) => item.invoiceNumber === invoiceNumber); if (!invoice) return; setSelectedInvoice(invoice); try { setPayments(await request<Payment[]>(`/api/billing/invoices/${invoiceNumber}/payments`)) } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to load payments.') } }
  const createPayment = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); if (!selectedInvoice) return; const data = new FormData(event.currentTarget); try { await request<Payment>(`/api/billing/invoices/${selectedInvoice.invoiceNumber}/payments`, { method: 'POST', body: JSON.stringify({ amount: Number(data.get('amount')), reference: data.get('reference') }) }); const refreshed = await request<Invoice[]>('/api/billing/invoices'); setInvoices(refreshed); const updated = refreshed.find((invoice) => invoice.invoiceNumber === selectedInvoice.invoiceNumber) ?? null; setSelectedInvoice(updated); if (updated) await openInvoice(updated.invoiceNumber); setShowPaymentForm(false); setNotice('Payment recorded successfully.') } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to record payment.') } }
  const openClaim = async (claim: Claim) => { setSelectedClaim(claim); setAttachments([]); try { setAttachments(await request<Attachment[]>(`/api/claims/${claim.claimNumber}/attachments`)) } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to load attachments.') } }
  const updateClaim = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); if (!selectedClaim) return; const data = new FormData(event.currentTarget); try { const updated = await request<Claim>(`/api/claims/${selectedClaim.claimNumber}`, { method: 'PATCH', body: JSON.stringify({ status: data.get('status'), reserveAmount: Number(data.get('reserveAmount')), paidAmount: Number(data.get('paidAmount')) }) }); setClaims((current) => current.map((claim) => claim.claimNumber === updated.claimNumber ? updated : claim)); setSelectedClaim(updated); setNotice('Claim updated successfully.') } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to update claim.') } }
  const approveClaim = async () => { if (!selectedClaim) return; try { const updated = await request<Claim>(`/api/claims/${selectedClaim.claimNumber}/approve`, { method: 'POST' }); setClaims((current) => current.map((claim) => claim.claimNumber === updated.claimNumber ? updated : claim)); setSelectedClaim(updated); setNotice('Claim approved successfully.') } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to approve claim.') } }
  const uploadAttachment = async (event: ChangeEvent<HTMLInputElement>) => { if (!selectedClaim || !event.target.files?.[0]) return; const body = new FormData(); body.append('file', event.target.files[0]); try { await request<Attachment>(`/api/claims/${selectedClaim.claimNumber}/attachments`, { method: 'POST', body }); setAttachments(await request<Attachment[]>(`/api/claims/${selectedClaim.claimNumber}/attachments`)); setNotice('Attachment uploaded successfully.') } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to upload attachment.') } event.target.value = '' }
  const createUser = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); try { const user = await request<User>('/api/users', { method: 'POST', body: JSON.stringify({ email: data.get('email'), displayName: data.get('displayName'), password: data.get('password'), role: data.get('role') }) }); setUsers((current) => [...current, user].sort((a, b) => a.email.localeCompare(b.email))); setShowUserForm(false); setNotice('User created successfully.') } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to create user.') } }
  const changeRole = async (user: User, role: string) => { try { const updated = await request<User>(`/api/users/${user.id}/role`, { method: 'PATCH', body: JSON.stringify({ role }) }); setUsers((current) => current.map((item) => item.id === updated.id ? updated : item)); setNotice(`${updated.displayName} is now ${updated.role}.`) } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to change role.') } }
  const updateOrganization = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); try { const updated = await request<Organization>('/api/organization', { method: 'PATCH', body: JSON.stringify({ name: data.get('name') }) }); setOrganization(updated); setNotice('Organization updated successfully.') } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to update organization.') } }
  const setPermission = async (userId: string, permission: string, enabled: boolean) => { try { await request<void>(`/api/users/${userId}/permissions/${permission}`, { method: enabled ? 'PUT' : 'DELETE' }); setPermissionsByUser((current) => ({ ...current, [userId]: enabled ? [...(current[userId] ?? []), permission] : (current[userId] ?? []).filter((value) => value !== permission) })); setNotice(`Permission ${enabled ? 'granted' : 'revoked'}.`) } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to update permission.') } }
  const revokeSession = async (sessionId: string) => { try { await request<void>(`/api/auth/sessions/${sessionId}`, { method: 'DELETE' }); setSessions((current) => current.map((session) => session.id === sessionId ? { ...session, revokedAt: new Date().toISOString() } : session)); setNotice('Session revoked.') } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to revoke session.') } }
  const deleteRecord = async (path: string, successMessage: string, onSuccess: () => void) => { try { await request<void>(path, { method: 'DELETE' }); onSuccess(); setNotice(successMessage); if (auth) await loadWorkspace(auth) } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to delete record.') } }
  const downloadAttachment = async (attachment: Attachment) => { if (!selectedClaim) return; try { const response = await authorizedFetch(auth!, `/api/claims/${selectedClaim.claimNumber}/attachments/${attachment.id}`); if (!response.ok) throw new Error('Unable to download attachment.'); const url = URL.createObjectURL(await response.blob()); const link = document.createElement('a'); link.href = url; link.download = attachment.fileName; link.click(); URL.revokeObjectURL(url) } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to download attachment.') } }
  const signOut = async () => { if (auth) { try { await authorizedFetch(auth, '/api/auth/logout', { method: 'POST', body: JSON.stringify({ refreshToken: auth.refreshToken }) }) } finally { clearSession() } } }

  if (!auth) return <AuthView mode={authMode} error={loginError} message={authMessage} resetToken={resetToken} onModeChange={changeAuthMode} onLogin={login} onRegister={register} onForgot={forgot} onReset={reset} />
  return <WorkspaceView auth={auth} activeSection={activeSection} setActiveSection={setActiveSection} policies={policies} claims={claims} customers={customers} invoices={invoices} payments={payments} sessions={sessions} auditEvents={auditEvents} organization={organization} permissionsByUser={permissionsByUser} users={users} selectedClaim={selectedClaim} selectedInvoice={selectedInvoice} attachments={attachments} notice={notice} showPolicyForm={showPolicyForm} showUserForm={showUserForm} showCustomerForm={showCustomerForm} showInvoiceForm={showInvoiceForm} showPaymentForm={showPaymentForm} setShowPolicyForm={setShowPolicyForm} setShowUserForm={setShowUserForm} setShowCustomerForm={setShowCustomerForm} setShowInvoiceForm={setShowInvoiceForm} setShowPaymentForm={setShowPaymentForm} setSelectedClaim={setSelectedClaim} setSelectedInvoice={setSelectedInvoice} setAttachments={setAttachments} money={money} dateTime={dateTime} fileSize={fileSize} createPolicy={createPolicy} createUser={createUser} createCustomer={createCustomer} createInvoice={createInvoice} createPayment={createPayment} changeRole={changeRole} updateOrganization={updateOrganization} setPermission={setPermission} openClaim={openClaim} updateClaim={updateClaim} approveClaim={approveClaim} uploadAttachment={uploadAttachment} openInvoice={openInvoice} revokeSession={revokeSession} deleteRecord={deleteRecord} downloadAttachment={downloadAttachment} signOut={signOut} />
}

export default App
