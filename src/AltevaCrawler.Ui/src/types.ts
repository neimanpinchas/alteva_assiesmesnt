export interface CrawlEvolutionPointDto {
  time: string;
  pages: number;
  bytes: number;
  pagesPerSecond?: number;
  kbPerSecond?: number;
}

export interface JobSummary {
  id: string;
  url: string;
  maxDepth: number;
  maxPagesLimit: number;
  status: string;
  totalPagesCrawled: number;
  discoveredLinksCount: number;
  totalBytesDownloaded: number;
  domainLinkRatio?: number;
  averagePagesPerSecond?: number;
  averageKbPerSecond?: number;
  totalDurationSeconds?: number;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
  failureReason?: string;
  statusHistory?: Array<{
    fromStatus: string;
    toStatus: string;
    transitionedAt: string;
    reason?: string;
  }>;
  progressHistory?: CrawlEvolutionPointDto[];
}

export interface TreeNodeDto {
  id: number;
  url: string;
  urlCrc32: number;
  domain: string;
  statusCode: number;
  simHash: number;
  simHashHex: string;
  outgoingLinksCount: number;
  onTheFlyDomainLinkRatio: number;
  isCrawled: boolean;
  errorKind?: string;
  errorMessage?: string;
  children: TreeNodeDto[];
}

export interface PageErrorDto {
  pageId?: number;
  url: string;
  statusCode: number;
  kind: string;
  message: string;
  occurredAt?: string;
}

export interface JobMetricsDto {
  domainLinkRatioPie: {
    internalLinks: number;
    externalLinks: number;
    totalPages: number;
    domainLinkRatio: number;
  };
  pageSharing: {
    uniqueToThisJob: number;
    sharedAcrossJobs: number;
  };
  bandwidthEfficiency: {
    totalBytesDownloaded: number;
    estimatedBytesSaved: number;
  };
  forecastedCompletionTime?: string;
}

export interface PageSearchResultDto {
  id: number;
  url: string;
  domain: string;
  jobId?: string;
  simHash: number;
  simHashHex: string;
  hammingDistance: number;
}

export interface PageContentDto {
  pageId: number;
  url: string;
  textContent: string;
}

export function getJobStatusBadgeClass(status: string): string {
  switch (status) {
    case 'Completed':
      return 'bg-emerald-900/60 text-emerald-300 border border-emerald-700';
    case 'Running':
      return 'bg-blue-900/60 text-blue-300 border border-blue-700 animate-pulse';
    case 'Pending':
    case 'Paused':
      return 'bg-amber-900/60 text-amber-300 border border-amber-700';
    case 'Canceled':
      return 'bg-rose-900/60 text-rose-300 border border-rose-700';
    case 'Failed':
      return 'bg-red-900/60 text-red-300 border border-red-700';
    default:
      return 'bg-slate-700 text-slate-300';
  }
}

export function getErrorKindLabel(kind: string): string {
  switch (kind) {
    case 'ssl': return 'SSL / TLS';
    case 'dns': return 'DNS';
    case 'timeout': return 'Timeout';
    case 'connection': return 'Connection';
    case 'http': return 'HTTP error';
    case 'non-html': return 'Not HTML';
    case 'storage': return 'Storage';
    default: return 'Error';
  }
}

export function getErrorKindBadgeClass(kind: string): string {
  switch (kind) {
    case 'ssl':
      return 'bg-red-900/60 text-red-300 border border-red-700';
    case 'dns':
    case 'connection':
    case 'timeout':
      return 'bg-orange-900/60 text-orange-300 border border-orange-700';
    case 'non-html':
      return 'bg-slate-700 text-slate-300 border border-slate-600';
    default:
      return 'bg-amber-900/60 text-amber-300 border border-amber-700';
  }
}

/** A fetched, stored HTML page. Mirrors the server's PageCrawledEvent.IsSuccess. */
export function isSuccessfulPageEvent(statusCode: number, byteSize: number): boolean {
  return statusCode >= 200 && statusCode < 300 && byteSize > 0;
}
