// Typed client for the Sentinel API. All endpoints are cookie-authenticated
// except login and shared reports.

async function f<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    ...init,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  });
  if (res.status === 401) {
    // session expired — bounce to login unless we're already there
    if (!location.pathname.startsWith('/login')) {
      location.href = '/login';
    }
    throw { status: 401, message: 'Not authenticated' };
  }
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw { status: res.status, ...body };
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

const post = (body: unknown): RequestInit => ({ method: 'POST', body: JSON.stringify(body) });
const put = (body: unknown): RequestInit => ({ method: 'PUT', body: JSON.stringify(body) });
const del: RequestInit = { method: 'DELETE' };

// ── Types (mirror C# models, camelCased by System.Text.Json) ──

export interface Me {
  id: string;
  username: string;
  role: string;
  displayName: string;
}

export interface CaseEvidence {
  timestamp: string;
  runId: string;
  summary: string;
  rawData: string;
}

export interface FraudCase {
  id: string;
  title: string;
  category: string;
  severity: string;
  status: string;
  confidence: number;
  firstSeen: string;
  lastSeen: string;
  occurrenceCount: number;
  affectedEntities: string[];
  evidence: CaseEvidence[];
  followUpQueries: string[];
  notes: string;
  resolution: string | null;
  workflowId: string | null;
}

export interface CaseOutcome {
  id: number;
  caseId: string;
  title: string;
  category: string;
  patternId: number | null;
  outcome: string;
  originalSeverity: string;
  confidence: number;
  affectedEntities: string;
  database: string;
  workflowId: string | null;
  resolution: string | null;
  resolvedBy: string;
  occurrenceCount: number;
  createdAt: string;
  resolvedAt: string;
}

export interface OutcomeStats {
  category: string;
  totalCases: number;
  confirmedFraud: number;
  falsePositives: number;
  inconclusive: number;
  autoResolved: number;
  falsePositiveRate: number;
}

export interface AdminUser {
  id: string;
  username: string;
  role: string;
  displayName: string;
  email: string | null;
  createdAt: string;
  lastLoginAt: string | null;
  isActive: boolean;
}

export interface WorkflowDefinition {
  id: string;
  name: string;
  description: string;
  actionType: string;
  cronExpression: string;
  timeZoneId: string;
  enabled: boolean;
  targetDatabase: string;
  emailSubject: string;
  emailRecipients: string;
  customPrompt: string;
  systemPrompt: string;
  isDeleted: boolean;
  createdAt: string;
  updatedAt: string;
  createdBy: string;
}

export interface RunSummary {
  runId: string;
  startedAt: string;
  finishedAt: string;
  iterations: number;
  inputTokens: number;
  outputTokens: number;
  casesCreated: number;
  casesResolved: number;
  alertsSent: number;
  status: string;
  triggeredBy: string;
  error: string | null;
  emailSubject: string | null;
  emailBody: string | null;
}

export interface RunLog {
  runId: string;
  iteration: number;
  toolName: string;
  args: string;
  result: string;
  durationMs: number;
  startedAt: string;
  logType: string;
}

export interface ActiveRunState {
  runId: string;
  status: string;
  triggeredBy: string;
  startedAtUtc: string;
}

export interface AgentMemory {
  id: number;
  term: string;
  definition: string;
  database: string | null;
  enabled: boolean;
  createdBy: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface DatabaseProduct {
  id: number;
  databaseName: string;
  displayName: string;
  description: string | null;
  enabled: boolean;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
}

export interface AuditLog {
  id: string;
  userId: string;
  username: string;
  action: string;
  resourceType: string;
  resourceId: string;
  details: string;
  ipAddress: string;
  timestamp: string;
}

export interface FeedbackRule {
  id: string;
  scope: string;
  scopeId: string | null;
  ruleType: string;
  matchValue: string;
  action: string;
  reason: string;
  createdBy: string;
  createdAt: string;
  expiresAt: string | null;
  hitCount: number;
  lastHitAt: string | null;
  sourceCaseId: string | null;
}

export interface EvidenceSource {
  id: number;
  name: string;
  evidenceDatabase: string;
  lipilaMerchantIds: string;
  lipilaPartnerId: number;
  joinMappings: string;
  tableDescriptions: string;
  evidenceChecks: string;
  notes: string;
  workflowId: string | null;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
  createdBy: string;
}

export interface FraudPattern {
  id: number;
  name: string;
  description: string;
  category: string;
  enabled: boolean;
  workflowId: string | null;
  createdAt: string;
  updatedAt: string;
  createdBy: string;
}

export interface QueryResult {
  label: string | null;
  sql: string | null;
  chartType: string;
  columns: string[];
  rows: Record<string, string>[];
  rowCount: number;
}

export interface AnalyticsResponse {
  success: boolean;
  sql: string | null;
  explanation: string | null;
  thinking: string | null;
  summary: string | null;
  riskLevel: string | null;
  findings: string[];
  recommendedActions: string[];
  error: string | null;
  chartType: string;
  columns: string[];
  rows: Record<string, string>[];
  rowCount: number;
  inputTokens: number;
  outputTokens: number;
  results: QueryResult[];
  pendingQuestion: string | null;
  pendingChoices: string[] | null;
  reportSent: boolean;
  emailSubject: string | null;
  emailBody: string | null;
}

export interface ChatEntry {
  role: string;
  content: string;
  response: AnalyticsResponse | null;
  timestamp: string;
}

export interface Conversation {
  id: string;
  title: string;
  database: string;
  mode: string;
  messages: ChatEntry[];
  createdAt: string;
  updatedAt: string;
  userId: string;
}

export interface StreamEvent {
  type: string;
  message: string;
  sql: string | null;
  attempt: number;
  timestamp: string;
}

export interface AnalyticsJob {
  jobId: string;
  status: 'pending' | 'running' | 'completed' | 'failed';
  result: AnalyticsResponse | null;
  error: string | null;
  conversationId: string;
  submittedAt: string;
  completedAt: string | null;
  streamEvents?: StreamEvent[];
}

export interface DashboardData {
  cases: FraudCase[];
  rules: FeedbackRule[];
  runs: RunSummary[];
  activeRuns: ActiveRunState[];
  workflows: WorkflowDefinition[];
}

// ── Client ──

export const api = {
  // auth
  login: (username: string, password: string) =>
    f<Me>('/api/auth/login', post({ username, password })),
  logout: () => fetch('/api/auth/logout', { method: 'POST', credentials: 'include' }),
  me: () => f<Me>('/api/auth/me'),
  signup: (req: { email: string; displayName: string; password: string; confirmPassword: string }) =>
    f<Me>('/api/auth/signup', post(req)),
  forgotPassword: (email: string) => f<{ sent: boolean }>('/api/auth/forgot-password', post({ email })),
  resetPassword: (token: string, password: string, confirmPassword: string) =>
    f<{ reset: boolean }>('/api/auth/reset-password', post({ token, password, confirmPassword })),

  // cases
  listCases: () => f<FraudCase[]>('/api/cases'),
  getCase: (id: string) => f<FraudCase>(`/api/cases/${id}`),
  deleteCase: (id: string) => f<void>(`/api/cases/${id}`, del),
  caseFeedback: (id: string, action: string, reason?: string, createRule?: Partial<FeedbackRule>) =>
    f<void>(`/api/cases/${id}/feedback`, post({ action, reason, createRule })),
  bulkResolve: (ids: string[], resolution: string) =>
    f<{ count: number }>('/api/cases/bulk-resolve', post({ ids, resolution })),
  relatedOutcomes: (id: string) => f<CaseOutcome[]>(`/api/cases/${id}/related-outcomes`),
  outcomeStats: () => f<OutcomeStats[]>('/api/outcome-stats'),

  // dashboard
  dashboard: () => f<DashboardData>('/api/dashboard'),

  // runs
  listRuns: (limit = 50, offset = 0) => f<RunSummary[]>(`/api/runs?limit=${limit}&offset=${offset}`),
  getRun: (runId: string) => f<{ summary: RunSummary; logs: RunLog[] }>(`/api/runs/${runId}`),
  activeRuns: () => f<ActiveRunState[]>('/api/runs/active'),
  triggerRun: () => f<void>('/api/runs/trigger', { method: 'POST' }),
  stopRun: (runId: string) => f<{ stopped: boolean }>(`/api/runs/${runId}/stop`, { method: 'POST' }),

  // workflows
  listWorkflows: () => f<WorkflowDefinition[]>('/api/workflows'),
  getWorkflow: (id: string) => f<WorkflowDefinition>(`/api/workflows/${id}`),
  saveWorkflow: (wf: Partial<WorkflowDefinition>) =>
    wf.id
      ? f<WorkflowDefinition>(`/api/workflows/${wf.id}`, put(wf))
      : f<WorkflowDefinition>('/api/workflows', post(wf)),
  deleteWorkflow: (id: string) => f<void>(`/api/workflows/${id}`, del),
  triggerWorkflow: (id: string) => f<void>(`/api/workflows/${id}/trigger`, { method: 'POST' }),
  workflowRuns: (id: string) => f<RunSummary[]>(`/api/workflows/${id}/runs`),
  workflowPatterns: (id: string) => f<FraudPattern[]>(`/api/workflows/${id}/patterns`),
  workflowEvidenceSources: (id: string) => f<EvidenceSource[]>(`/api/workflows/${id}/evidence-sources`),

  // knowledge
  listKnowledge: () => f<AgentMemory[]>('/api/knowledge'),
  saveKnowledge: (m: Partial<AgentMemory>) => f<AgentMemory>('/api/knowledge', post(m)),
  deleteKnowledge: (id: number) => f<void>(`/api/knowledge/${id}`, del),

  // products
  listProducts: () => f<DatabaseProduct[]>('/api/products'),
  enabledProducts: () => f<DatabaseProduct[]>('/api/products/enabled'),
  saveProduct: (p: Partial<DatabaseProduct>) => f<DatabaseProduct>('/api/products', post(p)),
  deleteProduct: (id: number) => f<void>(`/api/products/${id}`, del),

  // users
  listUsers: () => f<AdminUser[]>('/api/users/'),
  createUser: (u: { username: string; password: string; role: string; displayName: string; email?: string }) =>
    f<AdminUser>('/api/users/', post(u)),
  updateUser: (id: string, u: { role?: string; displayName?: string; isActive?: boolean; password?: string }) =>
    f<AdminUser>(`/api/users/${id}`, put(u)),
  deleteUser: (id: string) => f<void>(`/api/users/${id}`, del),

  // audit
  listAudit: (limit = 100, offset = 0) => f<AuditLog[]>(`/api/audit?limit=${limit}&offset=${offset}`),

  // rules
  listRules: () => f<FeedbackRule[]>('/api/rules'),
  saveRule: (r: Partial<FeedbackRule>) =>
    r.id ? f<FeedbackRule>(`/api/rules/${r.id}`, put(r)) : f<FeedbackRule>('/api/rules', post(r)),
  deleteRule: (id: string) => f<void>(`/api/rules/${id}`, del),

  // patterns
  listPatterns: () => f<FraudPattern[]>('/api/patterns/'),
  savePattern: (p: Partial<FraudPattern>) =>
    p.id ? f<FraudPattern>(`/api/patterns/${p.id}`, put(p)) : f<FraudPattern>('/api/patterns/', post(p)),
  deletePattern: (id: number) => f<void>(`/api/patterns/${id}`, del),

  // evidence sources
  listEvidenceSources: () => f<EvidenceSource[]>('/api/evidence-sources/'),
  saveEvidenceSource: (s: Partial<EvidenceSource>) =>
    s.id
      ? f<EvidenceSource>(`/api/evidence-sources/${s.id}`, put(s))
      : f<EvidenceSource>('/api/evidence-sources/', post(s)),
  deleteEvidenceSource: (id: number) => f<void>(`/api/evidence-sources/${id}`, del),

  // analytics / chat
  listDatabases: () => f<string[]>('/api/analytics/databases'),
  refreshSchema: () => f<{ message: string }>('/api/analytics/schema/refresh', { method: 'POST' }),
  listTables: (database: string) =>
    f<unknown>(`/api/analytics/tables?database=${encodeURIComponent(database)}`),
  listConversations: () => f<Conversation[]>('/api/analytics/conversations'),
  getConversation: (id: string) => f<Conversation>(`/api/analytics/conversations/${id}`),
  createConversation: (database: string) =>
    f<Conversation>('/api/analytics/conversations', post({ database })),
  renameConversation: (id: string, title: string) =>
    f<Conversation>(`/api/analytics/conversations/${id}`, put({ title })),
  deleteConversation: (id: string) => f<void>(`/api/analytics/conversations/${id}`, del),
  shareConversation: (id: string) =>
    f<{ shareId: string }>(`/api/analytics/conversations/${id}/share`, { method: 'POST' }),
  unshareConversation: (id: string) => f<void>(`/api/analytics/conversations/${id}/share`, del),
  ask: (prompt: string, database: string, conversationId?: string, mode?: string) =>
    f<{ jobId: string; conversationId: string; status: string }>(
      '/api/analytics/ask',
      post({ prompt, database, conversationId, mode })
    ),
  getJob: (jobId: string) => f<AnalyticsJob>(`/api/analytics/jobs/${jobId}`),

  // shared (public)
  getShared: (id: string) => f<Conversation>(`/api/shared/${id}`),
};
