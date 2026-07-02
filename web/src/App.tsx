import { useEffect, useState, createContext, useContext } from 'react';
import { Routes, Route, Navigate, NavLink, useNavigate, useLocation } from 'react-router-dom';
import { motion, AnimatePresence } from 'motion/react';
import { api, type Me } from './api';
import CommandPalette, { type PaletteItem } from './components/CommandPalette';
import Login from './features/Login';
import Dashboard from './features/Dashboard';
import Chat from './features/Chat';
import Cases from './features/Cases';
import CaseDetail from './features/CaseDetail';
import Workflows from './features/Workflows';
import WorkflowDetail from './features/WorkflowDetail';
import Runs from './features/Runs';
import RunDetail from './features/RunDetail';
import Rules from './features/Rules';
import Users from './features/Users';
import Audit from './features/Audit';
import Knowledge from './features/Knowledge';
import Products from './features/Products';
import SharedReport from './features/SharedReport';
import Signup from './features/Signup';
import ForgotPassword from './features/ForgotPassword';
import ResetPassword from './features/ResetPassword';

const MeContext = createContext<Me | null>(null);
export const useMe = () => useContext(MeContext);

type NavItem = { to: string; label: string; roles: string[] | null; icon: string };

const NAV: { section: string; items: NavItem[] }[] = [
  {
    section: 'Overview',
    items: [
      { to: '/', label: 'Dashboard', roles: ['admin', 'developer'], icon: 'M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6' },
      { to: '/chat', label: 'Chat', roles: null, icon: 'M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z' },
    ],
  },
  {
    section: 'Investigate',
    items: [
      { to: '/cases', label: 'Cases', roles: ['admin'], icon: 'M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2' },
      { to: '/runs', label: 'Runs', roles: ['admin'], icon: 'M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664zM21 12a9 9 0 11-18 0 9 9 0 0118 0z' },
      { to: '/audit', label: 'Audit Log', roles: ['admin'], icon: 'M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z' },
    ],
  },
  {
    section: 'Configure',
    items: [
      { to: '/workflows', label: 'Workflows', roles: ['admin'], icon: 'M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z' },
      { to: '/rules', label: 'Rules', roles: ['admin'], icon: 'M12 6V4m0 2a2 2 0 100 4m0-4a2 2 0 110 4m-6 8a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4m6 6v10m6-2a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4' },
      { to: '/knowledge', label: 'Knowledge Base', roles: ['admin'], icon: 'M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253' },
      { to: '/products', label: 'Products', roles: ['admin'], icon: 'M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4' },
    ],
  },
  {
    section: 'Admin',
    items: [
      { to: '/users', label: 'Users', roles: ['admin'], icon: 'M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z' },
    ],
  },
];

const COLLAPSE_KEY = 'sentinel.nav.collapsed';

function Sidebar({
  me,
  mobileOpen,
  onClose,
  collapsed,
  onToggleCollapse,
  onOpenPalette,
}: {
  me: Me;
  mobileOpen: boolean;
  onClose: () => void;
  collapsed: boolean;
  onToggleCollapse: () => void;
  onOpenPalette: () => void;
}) {
  const navigate = useNavigate();
  const sections = NAV.map((s) => ({
    ...s,
    items: s.items.filter((n) => !n.roles || n.roles.includes(me.role)),
  })).filter((s) => s.items.length > 0);

  const logout = async () => {
    await api.logout();
    navigate('/login');
  };

  return (
    <>
      {/* mobile backdrop */}
      {mobileOpen && <div className="fixed inset-0 z-20 bg-black/60 md:hidden" onClick={onClose} />}
      <motion.aside
        animate={{ width: collapsed ? 60 : 224 }}
        initial={false}
        transition={{ duration: 0.25, ease: [0.22, 1, 0.36, 1] }}
        className={`${mobileOpen ? 'flex' : 'hidden'} md:flex shrink-0 flex-col border-r border-gray-800/80 bg-gray-950/90 backdrop-blur fixed md:static inset-y-0 left-0 z-30 overflow-hidden`}
        style={{ width: collapsed ? 60 : 224 }}
      >
        {/* brand */}
        <div className="p-3 border-b border-gray-800/80">
          <div className={`flex items-center gap-2.5 ${collapsed ? 'justify-center' : ''}`}>
            <img src="https://hobbiton.tech/assets/logo2-7db998ca.png" alt="Hobbiton" className="h-7 w-7 rounded object-contain shrink-0" />
            {!collapsed && (
              <div className="min-w-0">
                <div className="font-display font-semibold text-sm text-white leading-none tracking-[0.14em]">SENTINEL</div>
                <div className="flex items-center gap-1.5 mt-1.5">
                  <span className="glow-dot" style={{ height: 5, width: 5 }} />
                  <span className="font-mono text-[9px] text-gray-500 leading-none uppercase tracking-wider">by Hobbiton</span>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* search / palette trigger */}
        <div className="p-2.5">
          <button
            onClick={onOpenPalette}
            title="Search (⌘K)"
            className={`w-full flex items-center gap-2 rounded-lg border border-gray-800 bg-gray-900/50 text-gray-500 hover:text-gray-300 hover:border-gray-700 transition-colors ${
              collapsed ? 'justify-center px-0 py-2' : 'px-2.5 py-1.5'
            }`}
          >
            <svg className="w-3.5 h-3.5 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-4.35-4.35M17 11a6 6 0 11-12 0 6 6 0 0112 0z" />
            </svg>
            {!collapsed && (
              <>
                <span className="text-xs flex-1 text-left">Search…</span>
                <kbd>⌘K</kbd>
              </>
            )}
          </button>
        </div>

        {/* nav */}
        <nav className="flex-1 overflow-y-auto overflow-x-hidden px-2.5 pb-2 text-sm" onClick={onClose}>
          {sections.map((s) => (
            <div key={s.section} className="mb-1.5">
              {collapsed ? (
                <div className="mx-2 my-2 border-t border-gray-800/70" />
              ) : (
                <div className="kicker px-2 pt-3 pb-1.5">{s.section}</div>
              )}
              <div className="space-y-0.5">
                {s.items.map((n) => (
                  <NavLink
                    key={n.to}
                    to={n.to}
                    end={n.to === '/'}
                    title={collapsed ? n.label : undefined}
                    className={({ isActive }) =>
                      `relative flex items-center gap-2.5 rounded-lg transition-colors duration-150 ${
                        collapsed ? 'justify-center px-0 py-2' : 'px-2.5 py-2'
                      } ${isActive ? 'text-white' : 'text-gray-400 hover:text-gray-200 hover:bg-gray-900/60'}`
                    }
                  >
                    {({ isActive }) => (
                      <>
                        {isActive && (
                          <motion.span
                            layoutId="nav-pill"
                            transition={{ duration: 0.25, ease: [0.22, 1, 0.36, 1] }}
                            className="absolute inset-0 rounded-lg bg-emerald-500/10 border border-emerald-500/20 shadow-[inset_0_1px_0_rgb(16_185_129/0.15)]"
                          />
                        )}
                        <svg className={`relative w-4 h-4 flex-shrink-0 ${isActive ? 'text-emerald-400' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d={n.icon} />
                        </svg>
                        {!collapsed && <span className="relative">{n.label}</span>}
                      </>
                    )}
                  </NavLink>
                ))}
              </div>
            </div>
          ))}
        </nav>

        {/* user / collapse */}
        <div className="p-2.5 border-t border-gray-800/80 text-xs space-y-2">
          {!collapsed ? (
            <div className="flex items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="text-gray-300 truncate">{me.displayName || me.username}</div>
                <div className="font-mono text-[10px] text-gray-600 uppercase tracking-wider">{me.role}</div>
              </div>
              <button
                onClick={logout}
                className="px-2 py-1 text-gray-500 hover:text-white border border-gray-800 rounded-md hover:bg-gray-900 transition-colors"
              >
                Logout
              </button>
            </div>
          ) : (
            <button onClick={logout} title="Logout" className="w-full flex justify-center py-1.5 text-gray-500 hover:text-white transition-colors">
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
              </svg>
            </button>
          )}
          <button
            onClick={onToggleCollapse}
            className="hidden md:flex w-full items-center justify-center py-1 text-gray-600 hover:text-gray-400 transition-colors"
            title={collapsed ? 'Expand' : 'Collapse'}
          >
            <svg className={`w-3.5 h-3.5 transition-transform duration-300 ${collapsed ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M11 19l-7-7 7-7m8 14l-7-7 7-7" />
            </svg>
          </button>
        </div>
      </motion.aside>
    </>
  );
}

function Shell({ me, children }: { me: Me; children: React.ReactNode }) {
  const [mobileNav, setMobileNav] = useState(false);
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(COLLAPSE_KEY) === '1');
  const [paletteOpen, setPaletteOpen] = useState(false);
  const location = useLocation();

  const toggleCollapse = () => {
    setCollapsed((c) => {
      localStorage.setItem(COLLAPSE_KEY, c ? '0' : '1');
      return !c;
    });
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setPaletteOpen((v) => !v);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const paletteItems: PaletteItem[] = NAV.flatMap((s) =>
    s.items
      .filter((n) => !n.roles || n.roles.includes(me.role))
      .map((n) => ({ label: n.label, to: n.to, icon: n.icon, section: s.section }))
  );

  return (
    <div className="h-screen bg-gray-950 flex flex-col relative">
      <div className="atmosphere" aria-hidden />
      <header className="md:hidden sticky top-0 z-20 bg-gray-950/95 backdrop-blur-sm border-b border-gray-800/80 px-4 py-2.5 flex items-center justify-between">
        <button onClick={() => setMobileNav((v) => !v)} className="p-1.5 -ml-1 rounded-lg text-gray-400 hover:text-white hover:bg-gray-800/60 transition-all">
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d="M4 6h16M4 12h16M4 18h16" />
          </svg>
        </button>
        <div className="flex items-center gap-2">
          <img src="https://hobbiton.tech/assets/logo2-7db998ca.png" alt="Hobbiton" className="h-6 w-6 rounded object-contain" />
          <span className="font-display font-semibold text-sm text-white tracking-[0.14em]">SENTINEL</span>
        </div>
        <button onClick={() => setPaletteOpen(true)} className="p-1.5 -mr-1 rounded-lg text-gray-400 hover:text-white hover:bg-gray-800/60 transition-all">
          <svg className="w-4.5 h-4.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-4.35-4.35M17 11a6 6 0 11-12 0 6 6 0 0112 0z" />
          </svg>
        </button>
      </header>
      <div className="flex flex-1 min-h-0 relative z-10">
        <Sidebar
          me={me}
          mobileOpen={mobileNav}
          onClose={() => setMobileNav(false)}
          collapsed={collapsed}
          onToggleCollapse={toggleCollapse}
          onOpenPalette={() => setPaletteOpen(true)}
        />
        <main className="flex-1 min-w-0 overflow-y-auto p-4 md:p-6">
          <motion.div
            key={location.pathname}
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.22, ease: 'easeOut' }}
            className="h-full"
          >
            {children}
          </motion.div>
        </main>
      </div>
      <AnimatePresence>
        {paletteOpen && <CommandPalette items={paletteItems} onClose={() => setPaletteOpen(false)} />}
      </AnimatePresence>
    </div>
  );
}

export default function App() {
  const [me, setMe] = useState<Me | null>(null);
  const [checking, setChecking] = useState(true);
  const location = useLocation();

  useEffect(() => {
    // shared reports are public — skip the session check
    if (location.pathname.startsWith('/shared/')) {
      setChecking(false);
      return;
    }
    api
      .me()
      .then(setMe)
      .catch(() => setMe(null))
      .finally(() => setChecking(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (checking) {
    return (
      <div className="h-screen flex items-center justify-center bg-gray-950">
        <div className="radar h-16 w-16 opacity-70" />
      </div>
    );
  }

  return (
    <MeContext.Provider value={me}>
      <Routes>
        <Route path="/shared/:id" element={<SharedReport />} />
        <Route path="/login" element={<Login onLogin={setMe} />} />
        <Route path="/signup" element={<Signup onLogin={setMe} />} />
        <Route path="/forgot-password" element={<ForgotPassword />} />
        <Route path="/reset-password" element={<ResetPassword />} />
        {me === null ? (
          <Route path="*" element={<Navigate to="/login" replace />} />
        ) : (
          <>
            <Route path="/" element={<Shell me={me}><Dashboard /></Shell>} />
            <Route path="/chat" element={<Shell me={me}><Chat /></Shell>} />
            <Route path="/cases" element={<Shell me={me}><Cases /></Shell>} />
            <Route path="/cases/:id" element={<Shell me={me}><CaseDetail /></Shell>} />
            <Route path="/workflows" element={<Shell me={me}><Workflows /></Shell>} />
            <Route path="/workflows/:id" element={<Shell me={me}><WorkflowDetail /></Shell>} />
            <Route path="/runs" element={<Shell me={me}><Runs /></Shell>} />
            <Route path="/runs/:id" element={<Shell me={me}><RunDetail /></Shell>} />
            <Route path="/rules" element={<Shell me={me}><Rules /></Shell>} />
            <Route path="/users" element={<Shell me={me}><Users /></Shell>} />
            <Route path="/audit" element={<Shell me={me}><Audit /></Shell>} />
            <Route path="/knowledge" element={<Shell me={me}><Knowledge /></Shell>} />
            <Route path="/products" element={<Shell me={me}><Products /></Shell>} />
            <Route path="*" element={<Navigate to="/chat" replace />} />
          </>
        )}
      </Routes>
    </MeContext.Provider>
  );
}
