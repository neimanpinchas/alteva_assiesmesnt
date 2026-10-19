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
  children: TreeNodeDto[];
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
  html: string;
}

export function getJobStatusBadgeClass(status: string): string {
  switch (status) {
    case 'Completed':
      return 'bg-emerald-900/60 text-emerald-300 border border-emerald-700';
    case 'Running':
      return 'bg-blue-900/60 text-blue-300 border border-blue-700 animate-pulse';
    case 'Pending':
      return 'bg-amber-900/60 text-amber-300 border border-amber-700';
    case 'Canceled':
      return 'bg-rose-900/60 text-rose-300 border border-rose-700';
    case 'Failed':
      return 'bg-red-900/60 text-red-300 border border-red-700';
    default:
      return 'bg-slate-700 text-slate-300';
  }
}
