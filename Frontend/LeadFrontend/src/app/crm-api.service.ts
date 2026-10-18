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
}

