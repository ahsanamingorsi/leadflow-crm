import { Injectable } from '@angular/core';

export type CurrentUser = {
  userId: string;
  displayName: string;
  email: string;
  workspaceId: string;
  workspaceName: string;
  currencyCode: string;
  timeZoneId: string;
  roles: string[];
  permissions: string[];
};

export type PageResult<T> = { items: T[]; totalCount: number; page: number; pageSize: number };
export type ApiCompany = { id: string; name: string; email?: string | null; phone?: string | null };
export type ApiContact = {
  id: string; displayName: string; email?: string | null; phone?: string | null;
  companyId?: string | null; ownerUserId?: string | null; rowVersion: number[];
  createdAtUtc: string; updatedAtUtc: string;
};
export type ApiStage = { id: string; pipelineId: string; name: string; position: number; kind: 'Open' | 'Won' | 'Lost'; rowVersion: number[] };
export type ApiPipeline = { id: string; name: string; isDefault: boolean; stages: ApiStage[]; rowVersion: number[] };
export type ApiOpportunity = {
  id: string; title: string; contactId: string; pipelineId: string; stageId: string;
  ownerUserId: string; value: number | null; valueHidden: boolean; currencyCode: string;
  expectedCloseDate?: string | null; description?: string | null; rowVersion: number[];
  createdAtUtc: string; updatedAtUtc: string;
};
export type ApiMember = { userId: string; displayName: string; email: string; isActive: boolean; roles: string[] };
export type ApiActivity = { id: string; eventType: string; summary: string; actorName: string; occurredAtUtc: string };
export type ApiAssignedLead = { id: string; contactId: string; contactName: string; email?: string | null; phone?: string | null; ownerUserId: string; ownerName: string; source: string; sourceDetail?: string | null; assignedAtUtc: string; acknowledgementDeadlineUtc: string; acknowledgedAtUtc?: string | null; lastActionAtUtc: string; escalationCount: number; isClosed: boolean; attentionReason?: string | null; rowVersion: number[] };
export type ApiIntegrationStatus = { provider: string; status: 'NotConfigured' | 'Configured' | 'Connected' | 'Error' | 'Disabled'; connectedAtUtc?: string | null; lastCheckedAtUtc?: string | null; lastError?: string | null };
export type ApiEmailSettings = { status: ApiIntegrationStatus['status']; host?: string | null; port?: number | null; fromAddress?: string | null; enableSsl: boolean; hasCredentials: boolean; hasInboundWebhook: boolean; connectedAtUtc?: string | null; lastError?: string | null };
export type ApiEmailMessage = { id: string; direction: 'Inbound' | 'Outbound'; status: 'Received' | 'Pending' | 'Sent' | 'Failed'; fromAddress: string; toAddress: string; subject: string; bodyText: string; messageId?: string | null; inReplyTo?: string | null; createdAtUtc: string; deliveredAtUtc?: string | null; lastError?: string | null };
export type ApiEmailConversation = { id: string; contactId?: string | null; leadId?: string | null; participantEmail: string; subject: string; lastMessageAtUtc: string; messages: ApiEmailMessage[] };
export type ApiPayment = { id: string; opportunityId: string; amount: number; currencyCode: string; status: 'Pending' | 'Paid' | 'Failed' | 'Expired' | 'Refunded'; checkoutUrl?: string | null; createdAtUtc: string; paidAtUtc?: string | null; expiresAtUtc?: string | null; refundedAtUtc?: string | null; lastError?: string | null };
export type ApiCalendarConnection = { connected: boolean; emailAddress?: string | null; calendarId?: string | null; connectedAtUtc?: string | null; lastCheckedAtUtc?: string | null; lastError?: string | null };
export type ApiCalendarAppointment = { id: string; contactId?: string | null; opportunityId?: string | null; title: string; description?: string | null; attendeeEmail?: string | null; startsAtUtc: string; endsAtUtc: string; timeZoneId: string; htmlLink?: string | null; status: 'Scheduled' | 'Cancelled' | 'Failed'; lastError?: string | null };

export class ApiError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

@Injectable({ providedIn: 'root' })
export class CrmApiService {
  private requestToken = '';

  private async request<T>(path: string, options: RequestInit = {}): Promise<T> {
    const response = await fetch(path, {
      ...options,
      credentials: 'include',
      headers: { Accept: 'application/json', ...options.headers }
    });
    if (response.status === 204) return undefined as T;
    const body = await response.json().catch(() => null);
    if (!response.ok) {
      const message = body?.error ?? body?.detail ?? body?.title ?? `Request failed (${response.status})`;
      throw new ApiError(message, response.status);
    }
    return body as T;
  }

  private async csrf(): Promise<string> {
    if (this.requestToken) return this.requestToken;
    const token = await this.request<{ requestToken: string }>('/api/auth/csrf');
    this.requestToken = token.requestToken;
    return this.requestToken;
  }

  private async mutate<T>(path: string, method: 'POST' | 'PUT' | 'DELETE', body?: unknown): Promise<T> {
    const token = await this.csrf();
    return this.request<T>(path, {
      method,
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
      ...(body === undefined ? {} : { body: JSON.stringify(body) })
    });
  }

  me(): Promise<CurrentUser> { return this.request('/api/auth/me'); }
  login(email: string, password: string, rememberMe: boolean): Promise<CurrentUser> {
    return this.mutate('/api/auth/login', 'POST', { email, password, rememberMe });
  }
  register(workspaceName: string, displayName: string, email: string, password: string): Promise<CurrentUser> {
    return this.mutate('/api/auth/register', 'POST', { workspaceName, displayName, email, password });
  }
  async logout(): Promise<void> { await this.mutate<void>('/api/auth/logout', 'POST'); this.requestToken = ''; }

  contacts(): Promise<PageResult<ApiContact>> { return this.request('/api/contacts?page=1&pageSize=100'); }
  companies(): Promise<PageResult<ApiCompany>> { return this.request('/api/companies?page=1&pageSize=100'); }
  opportunities(): Promise<PageResult<ApiOpportunity>> { return this.request('/api/opportunities?page=1&pageSize=100'); }
  pipelines(): Promise<PageResult<ApiPipeline>> { return this.request('/api/pipelines?page=1&pageSize=100'); }
  members(): Promise<ApiMember[]> { return this.request('/api/workspace/members'); }
  activities(opportunityId: string): Promise<PageResult<ApiActivity>> {
    return this.request(`/api/opportunities/${opportunityId}/activities?page=1&pageSize=50`);
  }
  assignedLead(id: string): Promise<ApiAssignedLead> { return this.request(`/api/leads/${id}`); }
  addOpportunityNote(opportunityId: string, text: string): Promise<ApiActivity> {
    return this.mutate(`/api/opportunities/${opportunityId}/notes`, 'POST', { text });
  }
  createCompany(name: string): Promise<ApiCompany> {
    return this.mutate('/api/companies', 'POST', { name, customFields: {} });
  }
  createContact(input: { displayName: string; email: string; companyId?: string; ownerUserId: string }): Promise<ApiContact> {
    return this.mutate('/api/contacts', 'POST', { ...input, customFields: {}, optedOut: false });
  }
  createOpportunity(input: { title: string; contactId: string; pipelineId: string; stageId: string; ownerUserId: string; value: number | null; description: string }): Promise<ApiOpportunity> {
    return this.mutate('/api/opportunities', 'POST', { ...input, customFields: {} });
  }
  moveOpportunity(opportunityId: string, stageId: string, rowVersion: number[]): Promise<ApiOpportunity> {
    return this.mutate(`/api/opportunities/${opportunityId}/move`, 'POST', { stageId, rowVersion });
  }

  integrationStatuses(): Promise<ApiIntegrationStatus[]> { return this.request('/api/integrations/status'); }
  emailSettings(): Promise<ApiEmailSettings> { return this.request('/api/integrations/email'); }
  saveEmailSettings(input: { host: string; port: number; fromAddress: string; enableSsl: boolean; username?: string; password?: string; inboundWebhookSecret?: string }): Promise<ApiEmailSettings> {
    return this.mutate('/api/integrations/email', 'PUT', input);
  }
  testEmail(): Promise<ApiEmailSettings> { return this.mutate('/api/integrations/email/test', 'POST'); }
  integrationHistory(provider?: string): Promise<Array<{ id: string; provider: string; eventType: string; status: string; summary: string; occurredAtUtc: string }>> {
    return this.request(`/api/integrations/history?limit=50${provider ? `&provider=${encodeURIComponent(provider)}` : ''}`);
  }
  emailConversations(search = ''): Promise<PageResult<ApiEmailConversation>> {
    return this.request(`/api/email/conversations?page=1&pageSize=100${search ? `&search=${encodeURIComponent(search)}` : ''}`);
  }
  emailConversation(id: string): Promise<ApiEmailConversation> { return this.request(`/api/email/conversations/${id}`); }
  replyToEmail(id: string, bodyText: string): Promise<ApiEmailMessage> {
    return this.mutate(`/api/email/conversations/${id}/reply`, 'POST', { bodyText });
  }
  retryEmailReply(conversationId: string, messageId: string): Promise<ApiEmailMessage> {
    return this.mutate(`/api/email/conversations/${conversationId}/messages/${messageId}/retry`, 'POST');
  }
  payments(opportunityId: string): Promise<ApiPayment[]> { return this.request(`/api/payments/opportunities/${opportunityId}`); }
  paymentBySession(sessionId: string): Promise<ApiPayment> { return this.request(`/api/payments/session/${encodeURIComponent(sessionId)}`); }
  createPaymentCheckout(opportunityId: string, idempotencyKey: string): Promise<ApiPayment> {
    return this.mutate('/api/payments/checkout', 'POST', { opportunityId, idempotencyKey });
  }
  calendarStatus(): Promise<ApiCalendarConnection> { return this.request('/api/integrations/calendar/status'); }
  calendarConnect(): Promise<{ authorizationUrl: string }> { return this.mutate('/api/integrations/calendar/connect', 'POST'); }
  disconnectCalendar(): Promise<void> { return this.mutate('/api/integrations/calendar/connection', 'DELETE'); }
  calendarAppointments(): Promise<ApiCalendarAppointment[]> { return this.request('/api/integrations/calendar/appointments'); }
  createAppointment(input: { title: string; description?: string; contactId?: string; opportunityId?: string; attendeeEmail?: string; startsAt: string; endsAt: string }): Promise<ApiCalendarAppointment> {
    return this.mutate('/api/integrations/calendar/appointments', 'POST', input);
  }
  rescheduleAppointment(id: string, input: { title: string; description?: string; contactId?: string; opportunityId?: string; attendeeEmail?: string; startsAt: string; endsAt: string }): Promise<ApiCalendarAppointment> {
    return this.mutate(`/api/integrations/calendar/appointments/${id}`, 'PUT', input);
  }
  cancelAppointment(id: string): Promise<ApiCalendarAppointment> {
    return this.mutate(`/api/integrations/calendar/appointments/${id}`, 'DELETE');
  }
  retryAppointment(id: string): Promise<ApiCalendarAppointment> {
    return this.mutate(`/api/integrations/calendar/appointments/${id}/retry`, 'POST');
  }
}

