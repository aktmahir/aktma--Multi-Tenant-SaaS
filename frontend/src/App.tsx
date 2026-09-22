import { useEffect, useState } from 'react'
import {
  Activity, ArrowUpRight, BarChart3, Bell, Building2, Check, ChevronDown,
  CircleHelp, CreditCard, FileClock, LayoutDashboard, Menu, Plus, Search,
  Settings2, ShieldCheck, Users, X,
} from 'lucide-react'
import { api, getSessionRole, type ApiAuditEntry, type ApiUser } from './api'
import { beginPkceLogin, completePkceLogin } from './pkce'

type View = 'Overview' | 'People' | 'Roles & access' | 'Audit log' | 'Tenant settings' | 'Billing'
type Member = { id: string; initials: string; name: string; email: string; role: string; status: 'Active' | 'Invited'; color: string }
type AuditItem = { actor: string; action: string; resource: string; time: string; tone: string }

const navigation: { label: View; icon: typeof LayoutDashboard }[] = [
  { label: 'Overview', icon: LayoutDashboard },
  { label: 'People', icon: Users },
  { label: 'Roles & access', icon: ShieldCheck },
  { label: 'Audit log', icon: FileClock },
  { label: 'Billing', icon: CreditCard },
]
const initialMembers: Member[] = [
  { id: 'demo-1', initials: 'LM', name: 'Lena Morgan', email: 'lena@acme.local', role: 'Tenant Admin', status: 'Active', color: 'bg-[#f3c5b6]' },
  { id: 'demo-2', initials: 'JK', name: 'Jonas Klein', email: 'jonas@acme.local', role: 'Tenant User', status: 'Active', color: 'bg-[#c7d8cb]' },
  { id: 'demo-3', initials: 'SC', name: 'Sofia Chen', email: 'sofia@acme.local', role: 'Read Only', status: 'Invited', color: 'bg-[#d7d0c5]' },
]
const auditItems: AuditItem[] = [
  { actor: 'Lena Morgan', action: 'invited a new member', resource: 'sofia@acme.local', time: '12 min ago', tone: 'bg-[#f3c5b6]' },
  { actor: 'Jonas Klein', action: 'updated the Analyst role', resource: 'Analyst', time: '48 min ago', tone: 'bg-[#c7d8cb]' },
  { actor: 'System', action: 'completed tenant backup', resource: 'Acme Corporation', time: '2 hrs ago', tone: 'bg-[#d7d0c5]' },
  { actor: 'Lena Morgan', action: 'updated tenant settings', resource: 'Branding color', time: 'Yesterday', tone: 'bg-[#e8d7a8]' },
]

function toMember(user: ApiUser): Member {
  const name = user.displayName || user.email
  return { id: user.id, initials: name.split(' ').map(part => part[0]).join('').slice(0, 2).toUpperCase(), name, email: user.email, role: user.role.replace(/([a-z])([A-Z])/g, '$1 $2'), status: user.isActive ? 'Active' : 'Invited', color: 'bg-[#c7d8cb]' }
}

function toAuditItem(entry: ApiAuditEntry): AuditItem {
  return { actor: entry.actorId ? entry.actorId.slice(0, 8) : 'System', action: entry.action, resource: entry.resource, time: new Date(entry.occurredAt).toLocaleString(), tone: 'bg-[#d7d0c5]' }
}

function exportMembersCsv(members: Member[]) {
  const escape = (value: string) => `"${value.replaceAll('"', '""')}"`
  const rows = [['Name', 'Email', 'Role', 'Status'], ...members.map(member => [member.name, member.email, member.role, member.status])]
  const csv = rows.map(row => row.map(escape).join(',')).join('\r\n')
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = 'northstar-members.csv'
  link.click()
  URL.revokeObjectURL(url)
}

function App() {
  const [authReady, setAuthReady] = useState(false)
  const [authenticated, setAuthenticated] = useState(false)
  const [active, setActive] = useState<View>('Overview')
  const [mobileOpen, setMobileOpen] = useState(false)
  const [inviteOpen, setInviteOpen] = useState(false)
  const [members, setMembers] = useState(initialMembers)
  const [inviteEmail, setInviteEmail] = useState('')
  const [inviteRole, setInviteRole] = useState('Tenant User')
  const [orgName, setOrgName] = useState('Acme Corporation')
  const [domain, setDomain] = useState('acme.localhost')
  const [brandColor, setBrandColor] = useState('#E45D42')
  const [saved, setSaved] = useState(false)
  const [dataError, setDataError] = useState('')
  const [auditData, setAuditData] = useState(auditItems)
  const [tenants, setTenants] = useState<{ id: string; slug: string; name: string; brandingColor: string }[]>([])
  const [activeTenant, setActiveTenant] = useState(localStorage.getItem('northstar.tenant') ?? 'acme-corp')
  const [sessionRole, setSessionRole] = useState<string | null>(null)
  const canManageUsers = sessionRole === 'SuperAdmin' || sessionRole === 'TenantAdmin'

  const refreshWorkspace = async () => {
    const accessToken = sessionStorage.getItem('northstar.access_token')
    if (!accessToken) {
      setDataError('')
      return
    }

    try {
      const [users, settings, audit, tenantList] = await Promise.all([
        api.users(),
        api.settings(),
        api.auditLog(),
        api.tenants(),
      ])
      setMembers(users.map(toMember))
      setOrgName(settings.name)
      setDomain(settings.domain ?? '')
      setBrandColor(settings.brandingColor)
      setAuditData(audit.entries.map(toAuditItem))
      setTenants(tenantList.filter(item => item.provisioningStatus === 'Provisioned'))
    } catch {
      setDataError('Live workspace data is unavailable. Retry when the API is reachable.')
    }
  }

  const handleLogout = async () => {
    try {
      await api.logout()
    } catch {
      // ignore server-side logout failure and clear the local session anyway
    }
    setAuthenticated(false)
    setDataError('')
    setActive('Overview')
    setSessionRole(null)
    sessionStorage.removeItem('northstar.access_token')
    localStorage.removeItem('northstar.tenant')
  }

  useEffect(() => {
    const establishSession = async () => {
      try {
        if (window.location.pathname === '/auth/callback') await completePkceLogin()
        const session = await api.session()
        const isAuthenticated = session.authenticated && !!sessionStorage.getItem('northstar.access_token')
        setAuthenticated(isAuthenticated)
        if (!isAuthenticated) {
          setSessionRole(null)
          setDataError('')
          return
        }
        setSessionRole(getSessionRole())
        try {
          await refreshWorkspace()
        } catch {
          setDataError('Live workspace data is unavailable. Retry when the API is reachable.')
        }
      } catch {
        setAuthenticated(false)
      } finally {
        setAuthReady(true)
      }
    }
    establishSession()
  }, [])

  useEffect(() => {
    const exportButton = Array.from(document.querySelectorAll('button')).find(button => button.textContent?.includes('Export CSV'))
    if (!exportButton) return
    const exportCurrentMembers = () => exportMembersCsv(members)
    exportButton.addEventListener('click', exportCurrentMembers)
    return () => exportButton.removeEventListener('click', exportCurrentMembers)
  }, [members])
  const navigate = (view: View) => { setActive(view); setMobileOpen(false) }
  const inviteMember = async () => {
    if (!canManageUsers || !inviteEmail.trim()) return
    const name = inviteEmail.split('@')[0].replace(/[._-]/g, ' ')
    try {
      const created = await api.createUser(inviteEmail, name, inviteRole.replace(' ', ''))
      setMembers(current => [...current, toMember(created)])
    } catch {
      setDataError('The invitation could not be sent. Retry when the API is reachable.')
    }
    setInviteEmail(''); setInviteOpen(false)
  }

  const saveSettings = async () => {
    if (!canManageUsers) {
      setDataError('Your role does not allow editing tenant settings.')
      return
    }

    try {
      const updated = await api.updateSettings({ name: orgName, domain, brandingColor: brandColor })
      setOrgName(updated.name); setDomain(updated.domain ?? ''); setBrandColor(updated.brandingColor)
    } catch {
      setDataError('Settings could not be saved. Check your tenant permissions.')
    }
    setSaved(true); setTimeout(() => setSaved(false), 2200)
  }

  if (!authReady) return <LoadingScreen />
  if (!authenticated) return <LoginScreen onAuthenticated={() => beginPkceLogin()} />

  return <div className="min-h-screen bg-[#f5f3ef] text-[#1f2421]">
    <Sidebar active={active} mobileOpen={mobileOpen} onNavigate={navigate} onClose={() => setMobileOpen(false)} />
    {mobileOpen && <button className="fixed inset-0 z-20 bg-[#1f2421]/20 lg:hidden" aria-label="Close navigation overlay" onClick={() => setMobileOpen(false)} />}
    <main className="lg:pl-[260px]">
      <Header
        onMenu={() => setMobileOpen(true)}
        onLogout={handleLogout}
        activeTenant={activeTenant}
        tenants={tenants}
        onSwitchTenant={(tenant) => {
          localStorage.setItem('northstar.tenant', tenant)
          setActiveTenant(tenant)
          void refreshWorkspace()
        }}
        canManageUsers={canManageUsers}
      />
      <div className="mx-auto max-w-[1180px] px-5 py-8 sm:px-9 lg:py-11">
        {dataError && <div role="status" className="mb-6 flex items-center justify-between gap-4 rounded-lg border border-[#ead5ce] bg-[#f9e6e1] px-4 py-3 text-xs font-semibold text-[#b84531]"><span>{dataError}</span><button onClick={() => void refreshWorkspace()} className="shrink-0 underline underline-offset-2">Retry</button></div>}
        {active === 'Overview' && <Overview onInvite={canManageUsers ? () => setInviteOpen(true) : undefined} onNavigate={navigate} canManageUsers={canManageUsers} />}
        {active === 'People' && <People members={members} onInvite={canManageUsers ? () => setInviteOpen(true) : undefined} canManageUsers={canManageUsers} />}
        {active === 'Roles & access' && <Roles />}
        {active === 'Audit log' && <AuditLog items={auditData} />}
        {active === 'Tenant settings' && <Settings orgName={orgName} setOrgName={setOrgName} domain={domain} setDomain={setDomain} brandColor={brandColor} setBrandColor={setBrandColor} saved={saved} onSave={saveSettings} canManageUsers={canManageUsers} />}
        {active === 'Billing' && <Billing />}
      </div>
    </main>
    {inviteOpen && <InviteModal email={inviteEmail} setEmail={setInviteEmail} role={inviteRole} setRole={setInviteRole} onClose={() => setInviteOpen(false)} onInvite={inviteMember} />}
  </div>
}

function Sidebar({ active, mobileOpen, onNavigate, onClose }: { active: View; mobileOpen: boolean; onNavigate: (view: View) => void; onClose: () => void }) {
  return <aside className={`fixed inset-y-0 left-0 z-30 flex w-[260px] flex-col border-r border-[#dedbd4] bg-[#fbfaf8] px-5 py-6 transition-transform lg:translate-x-0 ${mobileOpen ? 'translate-x-0' : '-translate-x-full'}`}>
    <div className="flex items-center justify-between px-2"><div className="flex items-center gap-3"><div className="flex h-9 w-9 items-center justify-center rounded-xl bg-[#e45d42] text-white"><Building2 size={18} /></div><div><div className="font-semibold tracking-[-.02em]">northstar</div><div className="text-[10px] font-semibold uppercase tracking-[.18em] text-[#8b908b]">control plane</div></div></div><button className="text-[#8b908b] lg:hidden" onClick={onClose} aria-label="Close navigation"><X size={19} /></button></div>
    <div className="mt-10 px-2 text-[10px] font-bold uppercase tracking-[.18em] text-[#a0a29d]">Workspace</div>
    <nav className="mt-3 space-y-1">{navigation.map(({ label, icon: Icon }) => <button key={label} onClick={() => onNavigate(label)} className={`flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-left text-sm font-medium transition ${active === label ? 'bg-[#f2e5df] text-[#b84531]' : 'text-[#656c67] hover:bg-[#f0ede8]'}`}><Icon size={17} strokeWidth={1.8} /><span>{label}</span>{label === 'Billing' && <span className="ml-auto rounded bg-[#e9e5de] px-1.5 py-0.5 text-[9px] font-bold uppercase tracking-wider text-[#8d928c]">Soon</span>}</button>)}</nav>
    <div className="mt-10 px-2 text-[10px] font-bold uppercase tracking-[.18em] text-[#a0a29d]">Manage</div><button onClick={() => onNavigate('Tenant settings')} className={`mt-3 flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-left text-sm font-medium ${active === 'Tenant settings' ? 'bg-[#f2e5df] text-[#b84531]' : 'text-[#656c67] hover:bg-[#f0ede8]'}`}><Settings2 size={17} strokeWidth={1.8} />Tenant settings</button>
    <div className="mt-auto rounded-xl border border-[#e2ded7] bg-[#f5f1ec] p-3"><div className="flex items-center gap-2"><CircleHelp size={15} className="text-[#e45d42]" /><span className="text-xs font-semibold">Need a hand?</span></div><p className="mt-2 text-xs leading-5 text-[#777e77]">Read the setup guide or talk to our team.</p><button className="mt-3 text-xs font-bold text-[#b84531]">Open documentation <ArrowUpRight size={13} className="ml-1 inline" /></button></div>
  </aside>
}

function Header({ onMenu, onLogout, activeTenant, tenants, onSwitchTenant, canManageUsers }: {
  onMenu: () => void
  onLogout: () => void
  activeTenant: string
  tenants: { id: string; slug: string; name: string; brandingColor: string }[]
  onSwitchTenant: (tenant: string) => void
  canManageUsers: boolean
}) {
  return <header className="flex h-[76px] items-center justify-between border-b border-[#dedbd4] bg-[#fbfaf8] px-5 sm:px-9"><button className="text-[#5c655f] lg:hidden" onClick={onMenu} aria-label="Open navigation"><Menu size={21} /></button><div className="relative hidden w-72 sm:block"><Search size={16} className="absolute left-3 top-2.5 text-[#a2a59f]" /><input className="h-9 w-full rounded-lg border border-[#e4e1db] bg-[#f7f5f2] pl-9 pr-3 text-xs outline-none placeholder:text-[#a2a59f] focus:border-[#e45d42]" placeholder="Search workspace" /></div><div className="ml-auto flex items-center gap-4"><button className="relative text-[#777e77]" aria-label="Notifications"><Bell size={18} /><span className="absolute -right-0.5 -top-0.5 h-1.5 w-1.5 rounded-full bg-[#e45d42]" /></button><div className="h-6 w-px bg-[#e2ded7]" /><div className="flex items-center gap-2 rounded-lg border border-[#dedbd4] bg-[#f7f5f2] px-2.5 py-1.5"><span className="text-[10px] font-bold uppercase tracking-[.14em] text-[#8b908b]">Tenant</span><select value={activeTenant} onChange={event => onSwitchTenant(event.target.value)} className="bg-transparent text-xs font-semibold text-[#3d443f] outline-none"><option value="">Choose tenant</option>{tenants.map(tenant => <option key={tenant.id} value={tenant.slug}>{tenant.name}</option>)}</select></div>{canManageUsers && <button onClick={onLogout} className="text-xs font-semibold text-[#656c67] transition hover:text-[#b84531]">Sign out</button>}<button className="flex items-center gap-2"><div className="flex h-8 w-8 items-center justify-center rounded-full bg-[#d7d0c5] text-xs font-bold text-[#49514a]">LM</div><span className="hidden text-sm font-medium sm:block">Lena Morgan</span><ChevronDown size={14} className="text-[#929790]" /></button></div></header> }

function PageTitle({ eyebrow, title, description, action }: { eyebrow: string; title: string; description: string; action?: React.ReactNode }) { return <div className="flex flex-col justify-between gap-5 sm:flex-row sm:items-end"><div><div className="flex items-center gap-2 text-xs font-semibold text-[#8a918b]"><span>Workspace</span><span>/</span><span className="text-[#b84531]">{eyebrow}</span></div><h1 className="mt-3 text-[31px] font-semibold tracking-[-.045em] text-[#202622]">{title}</h1><p className="mt-2 text-sm text-[#747b75]">{description}</p></div>{action}</div> }
const Button = ({ children, onClick, secondary = false, disabled = false }: { children: React.ReactNode; onClick?: () => void; secondary?: boolean; disabled?: boolean }) => <button onClick={onClick} disabled={disabled} className={`inline-flex w-fit items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-50 ${secondary ? 'border border-[#dedbd4] bg-[#fbfaf8] text-[#606760] hover:bg-[#f0ede8]' : 'bg-[#e45d42] text-white shadow-[0_4px_12px_rgba(228,93,66,.2)] hover:bg-[#c94d36]'}`}>{children}</button>

function Overview({ onInvite, onNavigate, canManageUsers }: { onInvite?: () => void; onNavigate: (view: View) => void; canManageUsers: boolean }) { return <><PageTitle eyebrow="Overview" title="Good morning, Lena" description="Here’s what’s happening across your organization today." action={onInvite ? <Button onClick={onInvite} disabled={!canManageUsers}><Plus size={16} /> Invite member</Button> : null} /><TenantBar /><section className="mt-7 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">{[['Active members', '24', '+12%', Users], ['Roles configured', '6', '+2', ShieldCheck], ['Events this month', '1,284', '+18.4%', Activity], ['Security score', '92', 'Excellent', BarChart3]].map(([label, value, trend, Icon]) => <Metric key={label as string} label={label as string} value={value as string} trend={trend as string} icon={Icon as typeof Users} />)}</section><section className="mt-7 grid gap-5 xl:grid-cols-[1.35fr_1fr]"><Setup /><ActivityCard onViewAll={() => onNavigate('Audit log')} /></section></> }
function TenantBar() { return <div className="mt-8 flex items-center justify-between rounded-xl border border-[#dedbd4] bg-[#fbfaf8] px-4 py-3"><div className="flex items-center gap-3"><div className="flex h-8 w-8 items-center justify-center rounded-lg bg-[#f2e5df] text-[#b84531]"><Building2 size={16} /></div><div><div className="text-[11px] font-semibold uppercase tracking-[.13em] text-[#9a9e98]">Current tenant</div><div className="mt-0.5 text-sm font-semibold">Acme Corporation</div></div></div><button className="flex items-center gap-2 rounded-md border border-[#dedbd4] px-3 py-1.5 text-xs font-semibold text-[#606760]">acme-corp <ChevronDown size={14} /></button></div> }
function Metric({ label, value, trend, icon: Icon }: { label: string; value: string; trend: string; icon: typeof Users }) { return <div className="rounded-xl border border-[#dedbd4] bg-[#fbfaf8] p-5"><div className="flex items-start justify-between"><span className="text-xs font-semibold text-[#828981]">{label}</span><Icon size={17} className="text-[#b2b5ae]" strokeWidth={1.7} /></div><div className="mt-4 flex items-end justify-between"><span className="text-[27px] font-semibold tracking-[-.04em]">{value}</span><span className="mb-1 text-[11px] font-bold text-[#5f876a]">{trend}</span></div></div> }
function Setup() { return <div className="rounded-xl border border-[#dedbd4] bg-[#fbfaf8] p-5 sm:p-6"><div className="flex items-start justify-between"><div><h2 className="font-semibold">Workspace setup</h2><p className="mt-1 text-xs text-[#858b85]">A few steps to get your tenant ready.</p></div><span className="text-xs font-semibold text-[#b84531]">75% complete</span></div><div className="mt-5 h-1.5 overflow-hidden rounded-full bg-[#ebe7e1]"><div className="h-full w-3/4 rounded-full bg-[#e45d42]" /></div><div className="mt-5 space-y-3">{['Create your organization', 'Invite your team', 'Configure roles and access', 'Connect your identity provider'].map((step, index) => <div key={step} className="flex items-center gap-3 text-sm"><div className={`flex h-5 w-5 items-center justify-center rounded-full ${index < 3 ? 'bg-[#5f876a] text-white' : 'border border-[#c9c8c2] text-transparent'}`}>{index < 3 && <Check size={12} strokeWidth={3} />}</div><span className={index < 3 ? 'text-[#8b918a] line-through' : 'font-medium'}>{step}</span></div>)}</div></div> }
function ActivityCard({ onViewAll }: { onViewAll: () => void }) { return <div className="rounded-xl border border-[#dedbd4] bg-[#fbfaf8] p-5 sm:p-6"><div className="flex items-center justify-between"><div><h2 className="font-semibold">Recent activity</h2><p className="mt-1 text-xs text-[#858b85]">Latest changes in your workspace.</p></div><button onClick={onViewAll} className="text-xs font-bold text-[#b84531]">View all</button></div><div className="mt-4 divide-y divide-[#ebe7e1]">{auditItems.slice(0, 3).map(item => <div className="flex items-center gap-3 py-3" key={item.resource}><div className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-[10px] font-bold text-[#49514a] ${item.tone}`}>{item.actor.split(' ').map(part => part[0]).join('').slice(0, 2)}</div><div className="min-w-0 flex-1"><p className="truncate text-xs"><span className="font-semibold">{item.actor}</span> <span className="text-[#7e857f]">{item.action}</span></p><p className="mt-1 text-[10px] text-[#a1a59f]">{item.time}</p></div></div>)}</div></div> }

function People({ members, onInvite, canManageUsers }: { members: Member[]; onInvite?: () => void; canManageUsers: boolean }) { return <><PageTitle eyebrow="People" title="Your team" description="Manage members and their access to Acme Corporation." action={onInvite ? <Button onClick={onInvite} disabled={!canManageUsers}><Plus size={16} /> Invite member</Button> : null} /><div className="mt-8 overflow-hidden rounded-xl border border-[#dedbd4] bg-[#fbfaf8]"><div className="flex items-center justify-between border-b border-[#ebe7e1] px-5 py-4"><span className="text-xs font-semibold text-[#828981]">{members.length} members</span><button className="text-xs font-bold text-[#b84531]">Export CSV</button></div><div className="overflow-x-auto"><table className="w-full min-w-[680px] text-left"><thead className="bg-[#f7f5f2] text-[10px] font-bold uppercase tracking-[.15em] text-[#9a9e98]"><tr><th className="px-5 py-3">Member</th><th className="px-5 py-3">Role</th><th className="px-5 py-3">Status</th><th className="px-5 py-3 text-right">Action</th></tr></thead><tbody className="divide-y divide-[#ebe7e1]">{members.map(member => <tr key={member.id}><td className="px-5 py-4"><div className="flex items-center gap-3"><div className={`flex h-8 w-8 items-center justify-center rounded-full text-[10px] font-bold text-[#49514a] ${member.color}`}>{member.initials}</div><div><div className="text-sm font-semibold">{member.name}</div><div className="text-xs text-[#858b85]">{member.email}</div></div></div></td><td className="px-5 py-4"><span className="rounded-md bg-[#f2e5df] px-2 py-1 text-[11px] font-semibold text-[#b84531]">{member.role}</span></td><td className="px-5 py-4"><span className="inline-flex items-center gap-1.5 text-xs text-[#687169]"><span className={`h-1.5 w-1.5 rounded-full ${member.status === 'Active' ? 'bg-[#5f876a]' : 'bg-[#d2a13e]'}`} />{member.status}</span></td><td className="px-5 py-4 text-right"><button disabled={!canManageUsers} className="text-xs font-bold text-[#858b85] hover:text-[#b84531] disabled:cursor-not-allowed disabled:opacity-50">Manage</button></td></tr>)}</tbody></table></div></div></> }

function Roles() {
  const [editing, setEditing] = useState<string | null>(null)
  const [savedRole, setSavedRole] = useState('')
  const roles = [['Tenant Admin', 'Full workspace management', 'Manage users, settings, and audit data'], ['Tenant User', 'Standard workspace access', 'Use tenant features and view members'], ['Read Only', 'View-only access', 'Read workspace data and activity'], ['SuperAdmin', 'Cross-tenant administration', 'Provision tenants and support operations']] as const
  return <><PageTitle eyebrow="Roles & access" title="Access model" description="Roles are scoped to this tenant and included in access tokens." /><div className="mt-8 grid gap-4 md:grid-cols-2">{roles.map(([name, summary, permissions], index) => <div className="rounded-xl border border-[#dedbd4] bg-[#fbfaf8] p-5" key={name}><div className="flex items-start justify-between"><div className="flex h-9 w-9 items-center justify-center rounded-lg bg-[#f2e5df] text-[#b84531]"><ShieldCheck size={17} /></div><button onClick={() => setEditing(name)} className="text-xs font-bold text-[#858b85] hover:text-[#b84531]">Edit</button></div><h2 className="mt-5 font-semibold">{name}</h2><p className="mt-1 text-xs text-[#858b85]">{summary}</p><div className="mt-4 border-t border-[#ebe7e1] pt-3 text-xs text-[#606760]">{permissions}</div>{index < 3 && <div className="mt-3 text-[10px] font-bold uppercase tracking-[.14em] text-[#5f876a]">System role</div>}{savedRole === name && <p className="mt-3 text-xs font-semibold text-[#5f876a]">Changes saved for this session.</p>}</div>)}</div>{editing && <div className="fixed inset-0 z-40 flex items-center justify-center bg-[#1f2421]/30 px-5"><div className="w-full max-w-md rounded-xl border border-[#dedbd4] bg-[#fbfaf8] p-6 shadow-xl"><h2 className="text-lg font-semibold">Edit {editing}</h2><p className="mt-2 text-sm text-[#747b75]">Review the tenant role policy before saving.</p><label className="mt-5 block"><span className="mb-2 block text-xs font-semibold text-[#606760]">Permission notes</span><textarea defaultValue="Manage users, settings, and audit data" className="min-h-24 w-full rounded-lg border border-[#dedbd4] bg-[#f7f5f2] p-3 text-sm outline-none focus:border-[#e45d42]" /></label><div className="mt-6 flex justify-end gap-3"><Button secondary onClick={() => setEditing(null)}>Cancel</Button><Button onClick={() => { setSavedRole(editing); setEditing(null) }}><Check size={16} /> Save changes</Button></div></div></div>}</>
}

function AuditLog({ items }: { items: AuditItem[] }) { return <><PageTitle eyebrow="Audit log" title="Activity history" description="A tamper-evident record of sensitive tenant actions." /><div className="mt-8 rounded-xl border border-[#dedbd4] bg-[#fbfaf8]"><div className="flex flex-wrap items-center gap-3 border-b border-[#ebe7e1] p-4"><div className="relative flex-1"><Search size={15} className="absolute left-3 top-2.5 text-[#a2a59f]" /><input className="h-9 w-full rounded-lg border border-[#e4e1db] bg-[#f7f5f2] pl-9 text-xs outline-none focus:border-[#e45d42]" placeholder="Filter activity" /></div><select className="h-9 rounded-lg border border-[#e4e1db] bg-[#f7f5f2] px-3 text-xs text-[#606760]"><option>All actions</option><option>User changes</option><option>Settings</option><option>Security</option></select></div><div className="divide-y divide-[#ebe7e1]">{items.length === 0 ? <div className="px-5 py-12 text-center text-sm text-[#858b85]">No activity has been recorded yet.</div> : items.map(item => <div className="flex items-center gap-4 px-5 py-4" key={`${item.resource}-${item.time}`}><div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-[10px] font-bold text-[#49514a] ${item.tone}`}>{item.actor.split(' ').map(part => part[0]).join('').slice(0, 2)}</div><div className="min-w-0 flex-1"><p className="text-sm"><span className="font-semibold">{item.actor}</span> <span className="text-[#737b74]">{item.action}</span></p><p className="mt-1 text-xs text-[#9a9e98]">{item.resource}</p></div><span className="shrink-0 text-[11px] text-[#9a9e98]">{item.time}</span></div>)}</div></div></> }

function Settings({ orgName, setOrgName, domain, setDomain, brandColor, setBrandColor, saved, onSave, canManageUsers }: { orgName: string; setOrgName: (value: string) => void; domain: string; setDomain: (value: string) => void; brandColor: string; setBrandColor: (value: string) => void; saved: boolean; onSave: () => void; canManageUsers: boolean }) { return <><PageTitle eyebrow="Tenant settings" title="Workspace settings" description="Control how this organization appears across Northstar." /><div className="mt-8 max-w-2xl rounded-xl border border-[#dedbd4] bg-[#fbfaf8] p-5 sm:p-7"><div className="flex items-center gap-3 border-b border-[#ebe7e1] pb-5"><div className="flex h-11 w-11 items-center justify-center rounded-xl text-white" style={{ backgroundColor: brandColor }}><Building2 size={20} /></div><div><h2 className="font-semibold">Organization profile</h2><p className="text-xs text-[#858b85]">Visible to members in this tenant.</p></div></div><div className="mt-6 space-y-5"><label className="block"><span className="mb-2 block text-xs font-semibold text-[#606760]">Organization name</span><input value={orgName} onChange={event => setOrgName(event.target.value)} disabled={!canManageUsers} className="h-10 w-full rounded-lg border border-[#dedbd4] bg-[#f7f5f2] px-3 text-sm outline-none focus:border-[#e45d42] disabled:cursor-not-allowed disabled:opacity-60" /></label><label className="block"><span className="mb-2 block text-xs font-semibold text-[#606760]">Workspace domain</span><input value={domain} onChange={event => setDomain(event.target.value)} disabled={!canManageUsers} className="h-10 w-full rounded-lg border border-[#dedbd4] bg-[#f7f5f2] px-3 text-sm outline-none focus:border-[#e45d42] disabled:cursor-not-allowed disabled:opacity-60" /></label><label className="block"><span className="mb-2 block text-xs font-semibold text-[#606760]">Branding color</span><div className="flex items-center gap-3"><input type="color" value={brandColor} onChange={event => setBrandColor(event.target.value.toUpperCase())} className="h-10 w-12 cursor-pointer rounded-lg border border-[#dedbd4] bg-[#f7f5f2] p-1" /><input value={brandColor} onChange={event => setBrandColor(event.target.value.toUpperCase())} className="h-10 w-32 rounded-lg border border-[#dedbd4] bg-[#f7f5f2] px-3 font-mono text-sm uppercase outline-none focus:border-[#e45d42]" /></div></label></div><div className="mt-7 flex items-center gap-3 border-t border-[#ebe7e1] pt-5"><Button onClick={onSave} disabled={!canManageUsers}>Save changes</Button>{saved && <span className="text-xs font-semibold text-[#5f876a]">Saved successfully</span>}</div></div></> }

function Billing() {
  const [selectedPlan, setSelectedPlan] = useState('Starter')
  const plans = [['Starter', 'For focused teams', '$0 / month'], ['Growth', 'For growing organizations', '$49 / month'], ['Scale', 'For advanced operations', 'Contact sales']]
  return <><PageTitle eyebrow="Billing" title="Plans and billing" description="Choose the plan that fits your organization." /><div className="mt-8 grid gap-4 md:grid-cols-3">{plans.map(([name, summary, price]) => <div key={name} className={`rounded-xl border bg-[#fbfaf8] p-5 ${selectedPlan === name ? 'border-[#e45d42] shadow-[0_4px_14px_rgba(228,93,66,.12)]' : 'border-[#dedbd4]'}`}><div className="flex items-start justify-between"><div><h2 className="font-semibold">{name}</h2><p className="mt-1 text-xs text-[#858b85]">{summary}</p></div>{selectedPlan === name && <Check size={17} className="text-[#5f876a]" />}</div><p className="mt-7 text-2xl font-semibold tracking-[-.04em]">{price}</p><button onClick={() => setSelectedPlan(name)} className="mt-6 w-full rounded-lg border border-[#dedbd4] px-3 py-2 text-xs font-bold text-[#606760] hover:bg-[#f0ede8]">{selectedPlan === name ? 'Current plan' : 'Select plan'}</button></div>)}</div><div className="mt-5 flex items-center gap-3 rounded-xl border border-[#dedbd4] bg-[#fbfaf8] p-5 text-sm text-[#747b75]"><CreditCard size={18} className="text-[#e45d42]" /><span>Payment processing is not configured. Your {selectedPlan} selection is saved for this session.</span></div></>
}

function LoadingScreen() { return <div className="flex min-h-screen items-center justify-center bg-[#f5f3ef]"><div className="flex items-center gap-3 text-sm font-semibold text-[#656c67]"><span className="h-2 w-2 animate-pulse rounded-full bg-[#e45d42]" /> Checking your session</div></div> }

function LoginScreen({ onAuthenticated }: { onAuthenticated: () => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault(); setError(''); setLoading(true)
    try { await api.login(email, password, true); onAuthenticated() }
    catch (loginError) { setError(loginError instanceof Error ? loginError.message : 'Unable to sign in.') }
    finally { setLoading(false) }
  }

  useEffect(() => {
    const button = Array.from(document.querySelectorAll('button')).find(item => item.textContent?.includes('Forgot password'))
    if (!button) return
    const requestReset = () => {
      const resetEmail = window.prompt('Enter your organization email address')
      if (!resetEmail) return
      void api.forgotPassword(resetEmail).then(() => window.alert('If that account exists, password reset instructions will be sent.')).catch(() => setError('Unable to request a password reset.'))
    }
    button.addEventListener('click', requestReset)
    return () => button.removeEventListener('click', requestReset)
  }, [])

  return <div className="flex min-h-screen items-center justify-center bg-[#f5f3ef] px-5"><div className="w-full max-w-[420px]"><div className="mb-8 flex items-center justify-center gap-3"><div className="flex h-10 w-10 items-center justify-center rounded-xl bg-[#e45d42] text-white"><Building2 size={19} /></div><div><div className="font-semibold tracking-[-.02em]">northstar</div><div className="text-[10px] font-semibold uppercase tracking-[.18em] text-[#8b908b]">control plane</div></div></div><div className="rounded-xl border border-[#dedbd4] bg-[#fbfaf8] p-7 shadow-[0_8px_30px_rgba(37,38,33,.05)]"><h1 className="text-2xl font-semibold tracking-[-.04em]">Welcome back</h1><p className="mt-2 text-sm text-[#747b75]">Sign in to manage your workspace.</p><form onSubmit={submit} className="mt-7 space-y-4"><label className="block"><span className="mb-2 block text-xs font-semibold text-[#606760]">Email address</span><input required type="email" value={email} onChange={event => setEmail(event.target.value)} className="h-11 w-full rounded-lg border border-[#dedbd4] bg-[#f7f5f2] px-3 text-sm outline-none focus:border-[#e45d42]" autoComplete="email" /></label><label className="block"><span className="mb-2 block text-xs font-semibold text-[#606760]">Password</span><input required type="password" value={password} onChange={event => setPassword(event.target.value)} className="h-11 w-full rounded-lg border border-[#dedbd4] bg-[#f7f5f2] px-3 text-sm outline-none focus:border-[#e45d42]" autoComplete="current-password" /></label>{error && <p role="alert" className="rounded-lg bg-[#f9e6e1] px-3 py-2 text-xs font-semibold text-[#b84531]">{error}</p>}<button disabled={loading} className="flex h-11 w-full items-center justify-center rounded-lg bg-[#e45d42] text-sm font-semibold text-white transition hover:bg-[#c94d36] disabled:cursor-wait disabled:opacity-60">{loading ? 'Signing in…' : 'Sign in'}</button></form><div className="mt-6 flex items-center justify-between text-xs text-[#858b85]"><button className="font-semibold text-[#b84531]">Forgot password?</button><span>Protected by Northstar Identity</span></div></div><p className="mt-5 text-center text-xs text-[#9a9e98]">Use your organization account to continue.</p></div></div>
}

function InviteModal({ email, setEmail, role, setRole, onClose, onInvite }: { email: string; setEmail: (value: string) => void; role: string; setRole: (value: string) => void; onClose: () => void; onInvite: () => void }) { return <div className="fixed inset-0 z-40 flex items-center justify-center bg-[#1f2421]/30 px-5"><div className="w-full max-w-md rounded-xl border border-[#dedbd4] bg-[#fbfaf8] p-6 shadow-xl"><div className="flex items-start justify-between"><div><h2 className="text-lg font-semibold">Invite a member</h2><p className="mt-1 text-xs text-[#858b85]">They’ll receive an invitation to Acme Corporation.</p></div><button onClick={onClose} aria-label="Close invite dialog" className="text-[#858b85]"><X size={18} /></button></div><div className="mt-6 space-y-4"><label className="block"><span className="mb-2 block text-xs font-semibold text-[#606760]">Email address</span><input autoFocus value={email} onChange={event => setEmail(event.target.value)} type="email" placeholder="name@company.com" className="h-10 w-full rounded-lg border border-[#dedbd4] bg-[#f7f5f2] px-3 text-sm outline-none focus:border-[#e45d42]" /></label><label className="block"><span className="mb-2 block text-xs font-semibold text-[#606760]">Role</span><select value={role} onChange={event => setRole(event.target.value)} className="h-10 w-full rounded-lg border border-[#dedbd4] bg-[#f7f5f2] px-3 text-sm outline-none focus:border-[#e45d42]"><option>Tenant User</option><option>Tenant Admin</option><option>Read Only</option></select></label></div><div className="mt-7 flex justify-end gap-3"><Button secondary onClick={onClose}>Cancel</Button><Button onClick={onInvite}><Plus size={16} /> Send invite</Button></div></div></div> }

export default App
