// @ts-ignore
import './haxeGenerated/shared.js';
import { JobSummary, TreeNodeDto, JobMetricsDto, PageSearchResultDto, PageContentDto, PageErrorDto } from './types';

const API_BASE = 'api';
let authToken: string | null = null;

export async function getAuthToken(): Promise<string> {
  if (authToken) return authToken;
  const res = await fetch(`${API_BASE}/auth/token`, { method: 'POST' });
  const data = await res.json();
  authToken = data.token;
  return authToken!;
}

async function fetchWithAuth(url: string, options: RequestInit = {}): Promise<Response> {
  const token = await getAuthToken();
  const headers = {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${token}`,
    ...(options.headers || {})
  };
  return fetch(url, { ...options, headers });
}

export async function createJob(url: string, maxDepth: number = 2, maxPagesLimit: number = 200): Promise<{ jobId: string }> {
  const res = await fetchWithAuth(`${API_BASE}/jobs`, {
    method: 'POST',
    body: JSON.stringify({ url, maxDepth, maxPagesLimit })
  });
  if (!res.ok) {
    const err = await res.json();
    throw new Error(err.error || 'Failed to create job');
  }
  return res.json();
}

export async function getJob(jobId: string): Promise<JobSummary> {
  const res = await fetchWithAuth(`${API_BASE}/jobs/${jobId}`);
  if (!res.ok) throw new Error('Failed to fetch job details');
  return res.json();
}

export async function getJobTree(jobId: string, parentPageId?: number, depth: number = 3): Promise<TreeNodeDto[]> {
  const params = new URLSearchParams({ depth: depth.toString() });
  if (parentPageId) params.append('parentPageId', parentPageId.toString());
  
  const res = await fetchWithAuth(`${API_BASE}/jobs/${jobId}/tree?${params.toString()}`);
  if (!res.ok) throw new Error('Failed to fetch tree nodes');
  return res.json();
}

export async function getJobMetrics(jobId: string): Promise<JobMetricsDto> {
  const res = await fetchWithAuth(`${API_BASE}/jobs/${jobId}/metrics`);
  if (!res.ok) throw new Error('Failed to fetch job metrics');
  return res.json();
}

export async function globalSearch(query: string): Promise<PageSearchResultDto[]> {
  const params = new URLSearchParams({ q: query });
  const res = await fetchWithAuth(`${API_BASE}/jobs/global-search?${params.toString()}`);
  if (!res.ok) throw new Error('Failed to perform global search');
  return res.json();
}

export async function searchJobPages(jobId: string, query: string): Promise<PageSearchResultDto[]> {
  const params = new URLSearchParams({ q: query });
  const res = await fetchWithAuth(`${API_BASE}/jobs/${jobId}/search?${params.toString()}`);
  if (!res.ok) throw new Error('Failed to perform keyword search');
  return res.json();
}

export async function getPageContent(jobId: string, pageId: number): Promise<PageContentDto> {
  const res = await fetchWithAuth(`${API_BASE}/jobs/${jobId}/pages/${pageId}/content`);
  if (!res.ok) throw new Error('Failed to load page content');
  return res.json();
}

export async function getJobHistory(page: number = 1, pageSize: number = 10) {
  const res = await fetchWithAuth(`${API_BASE}/jobs?page=${page}&pageSize=${pageSize}`);
  if (!res.ok) throw new Error('Failed to fetch history');
  return res.json();
}

export async function pauseJob(jobId: string) {
  const res = await fetchWithAuth(`${API_BASE}/jobs/${jobId}/pause`, { method: 'POST' });
  if (!res.ok) throw new Error('Failed to pause job');
  return res.json();
}

export async function resumeJob(jobId: string) {
  const res = await fetchWithAuth(`${API_BASE}/jobs/${jobId}/resume`, { method: 'POST' });
  if (!res.ok) throw new Error('Failed to resume job');
  return res.json();
}

export async function cancelJob(jobId: string) {
  const res = await fetchWithAuth(`${API_BASE}/jobs/${jobId}/cancel`, { method: 'POST' });
  if (!res.ok) throw new Error('Failed to cancel job');
  return res.json();
}

export async function deleteJob(jobId: string) {
  const res = await fetchWithAuth(`${API_BASE}/jobs/${jobId}`, { method: 'DELETE' });
  if (!res.ok) throw new Error('Failed to delete job');
  return res.json();
}

export async function getJobErrors(jobId: string): Promise<PageErrorDto[]> {
  const res = await fetchWithAuth(`${API_BASE}/jobs/${jobId}/errors`);
  if (!res.ok) throw new Error('Failed to fetch crawl errors');
  return res.json();
}
