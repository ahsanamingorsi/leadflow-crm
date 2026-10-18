import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';

const base = process.env.PHASE3_BASE_URL ?? 'https://localhost:7291';
const server = process.env.PHASE3_SQL_SERVER ?? 'AHSAN-DESKTOP\\SQLEXPRESS';
const database = process.env.PHASE3_SQL_DATABASE ?? 'LeadFlowCrmDb';
const runId = randomUUID().replaceAll('-', '');
let workspaceId;
let checks = 0;

class Api {
  cookies = new Map();
  async request(method, path, data, expected = 200, extraHeaders = {}, csrf = true) {
    const headers = { Accept: 'application/json', ...extraHeaders };
    if (method !== 'GET' && csrf) {
      const token = await this.request('GET', '/api/auth/csrf');
      headers['X-XSRF-TOKEN'] = token.requestToken;
    }
    const cookie = [...this.cookies].map(([k, v]) => `${k}=${v}`).join('; ');
    if (cookie) headers.Cookie = cookie;
    let body;
    if (data !== undefined) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(data); }
    const response = await fetch(base + path, { method, headers, body });
    for (const value of response.headers.getSetCookie()) {
      const [pair] = value.split(';');
      const eq = pair.indexOf('=');
      if (eq > 0) this.cookies.set(pair.slice(0, eq), pair.slice(eq + 1));
    }
    const text = await response.text();
    if (response.status !== expected) throw new Error(`${method} ${path}: expected ${expected}, got ${response.status}: ${text.slice(0, 900)}`);
    checks++;
    return text ? JSON.parse(text) : null;
  }
}

function sql(command) {
  const result = spawnSync('sqlcmd', ['-S', server, '-d', database, '-E', '-C', '-I', '-b', '-Q', command], { encoding: 'utf8', timeout: 45000 });
  if (result.status !== 0) throw new Error(`SQL fixture operation failed: ${result.stdout}${result.stderr}`);
}

function check(condition, message) { if (!condition) throw new Error(message); checks++; }

function cleanup() {
  if (!workspaceId) return;
  const wid = workspaceId;
  sql(`SET XACT_ABORT ON; BEGIN TRANSACTION;
DECLARE @w TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT INTO @w SELECT Id FROM Workspaces WHERE Id='${wid}' AND Name='Phase3Smoke-${runId}';
DELETE FROM LeadUserNotifications WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM LeadNotifications WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM LeadAssignmentHistory WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM Leads WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM LeadIntakes WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM EmployeeAvailabilities WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM LeadAssignmentConfigurations WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM CrmActivities WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM CrmOpportunities WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM CrmContacts WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM CrmCompanies WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM CrmCustomFields WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM CrmPipelineStages WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM CrmPipelines WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM AuditLogs WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM WorkspaceInvitations WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM RolePermissions WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM AspNetUserRoles WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM AspNetUsers WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM AspNetRoles WHERE WorkspaceId IN (SELECT Id FROM @w);
DELETE FROM Workspaces WHERE Id IN (SELECT Id FROM @w); COMMIT;`);
}

async function main() {
  sql('SET NOCOUNT ON; SELECT 1;');
  const owner = new Api();
  const me = await owner.request('POST', '/api/auth/register', {
    workspaceName: `Phase3Smoke-${runId}`, displayName: 'Phase 3 owner',
    email: `${runId}@example.invalid`, password: `Aa1!${randomUUID()}`
  }, 201);
  workspaceId = me.workspaceId;
  check(me.currencyCode === 'PKR' && me.timeZoneId === 'Asia/Karachi', 'Pakistan workspace defaults');
  for (const permission of ['leads.view', 'leads.view-all', 'leads.create', 'leads.reassign', 'leads.availability.manage'])
    check(me.permissions.includes(permission), `Owner gets ${permission}`);

  const settings = await owner.request('GET', '/api/workspace/assignment/settings');
  check(JSON.stringify(settings.escalationIntervalsSeconds) === '[300]', 'Default escalation settings');
  const availability = await owner.request('GET', '/api/workspace/assignment/availability');
  check(availability.length === 1 && availability[0].status === 'Available', 'New user is available');

  const key = `phase3:${randomUUID()}`;
  const leadRequest = { contactName: 'Leadflow Smoke Contact', email: `lead-${runId}@example.invalid`,
    phone: '+923001234567', companyName: 'Phase 3 Smoke Co', message: 'Smoke test' };
  const first = await owner.request('POST', '/api/leads/intake', leadRequest, 201, { 'Idempotency-Key': key });
  check(first.status === 'Assigned' && first.leadId, 'Manual lead assigned');
  const duplicate = await owner.request('POST', '/api/leads/intake', leadRequest, 200, { 'Idempotency-Key': key });
  check(duplicate.isDuplicate && duplicate.leadId === first.leadId, 'Intake idempotency');
  const list = await owner.request('GET', '/api/leads');
  check(list.items.some(x => x.id === first.leadId && x.contactName === leadRequest.contactName), 'Lead list contact fields');
  const assignments = await owner.request('GET', `/api/leads/${first.leadId}/assignments`);
  check(assignments.length === 1 && assignments[0].reason === 'InitialAssignment', 'Assignment history');
  const lead = await owner.request('GET', `/api/leads/${first.leadId}`);
  await owner.request('POST', `/api/leads/${first.leadId}/acknowledge`, { rowVersion: lead.rowVersion }, 200);
  check((await owner.request('GET', '/api/leads?acknowledged=true')).totalCount === 1, 'Acknowledged filter');
  check((await owner.request('GET', '/api/workspace/assignment/my-notifications')).totalCount >= 1, 'In-app notification');

  const keyResponse = await owner.request('POST', '/api/workspace/assignment/public-intake-key');
  const publicClient = new Api();
  const publicResult = await publicClient.request('POST', '/api/public/leads', {
    contactName: 'Public Intake Smoke', email: `public-${runId}@example.invalid`
  }, 201, { 'X-Leadflow-Intake-Key': keyResponse.publicKey, 'Idempotency-Key': `public:${randomUUID()}` }, false);
  check(publicResult.status === 'Assigned', 'Public intake assigned');
  await publicClient.request('POST', '/api/public/leads', {
    contactName: 'Bad Public Key', email: `bad-${runId}@example.invalid`
  }, 401, { 'X-Leadflow-Intake-Key': 'invalid', 'Idempotency-Key': `bad:${randomUUID()}` }, false);
}

try {
  await main();
  console.log(`Phase 3 smoke checks passed: ${checks}`);
} finally {
  cleanup();
}
