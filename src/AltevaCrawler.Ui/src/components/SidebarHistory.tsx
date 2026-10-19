import React, { useEffect, useState } from 'react';
import { getJobHistory } from '../api';
import { JobSummary, getJobStatusBadgeClass } from '../types';
import { History, PlusCircle } from 'lucide-react';

interface SidebarHistoryProps {
  selectedJobId: string | null;
  onSelectJob: (jobId: string) => void;
  onNewCrawl: () => void;
}

export const SidebarHistory: React.FC<SidebarHistoryProps> = ({
  selectedJobId,
  onSelectJob,
  onNewCrawl
}) => {
  const [jobs, setJobs] = useState<JobSummary[]>([]);

  useEffect(() => {
    loadJobs();
    const interval = setInterval(loadJobs, 5000);
    return () => clearInterval(interval);
  }, []);

  const loadJobs = async () => {
    try {
      const res = await getJobHistory(1, 20);
      setJobs(res.items);
    } catch (err) {
      console.error(err);
    }
  };

  return (
    <aside className="w-80 bg-slate-800/90 border-r border-slate-700 flex flex-col h-[calc(100vh-65px)] flex-shrink-0">
      <div className="p-4 border-b border-slate-700 flex items-center justify-between">
        <div className="flex items-center space-x-2">
          <History className="w-5 h-5 text-cyan-400" />
          <h3 className="font-bold text-white text-sm">Crawl Jobs History</h3>
        </div>
        <button
          onClick={onNewCrawl}
          className="p-1.5 bg-cyan-600 hover:bg-cyan-500 text-white rounded-lg text-xs font-semibold flex items-center space-x-1 transition"
        >
          <PlusCircle className="w-3.5 h-3.5" />
          <span>New</span>
        </button>
      </div>

      <div className="flex-1 overflow-y-auto p-3 space-y-2">
        {jobs.map((j) => (
          <div
            key={j.id}
            onClick={() => onSelectJob(j.id)}
            className={`p-3 rounded-lg border cursor-pointer transition ${
              selectedJobId === j.id
                ? 'bg-slate-700 border-cyan-500 shadow-md'
                : 'bg-slate-900/60 border-slate-700/60 hover:border-slate-500'
            }`}
          >
            <div className="flex items-center justify-between mb-1">
              <span className="font-semibold text-xs text-white truncate max-w-[170px]">{j.url}</span>
              <span className={`px-2 py-0.5 text-[10px] rounded-full font-medium ${getJobStatusBadgeClass(j.status)}`}>
                {j.status}
              </span>
            </div>
            <div className="flex items-center justify-between text-[11px] text-slate-400">
              <span>Pages: {j.totalPagesCrawled}</span>
              <span className="text-cyan-400 font-medium">Domain: {((j.domainLinkRatio || 0) * 100).toFixed(0)}%</span>
            </div>
          </div>
        ))}
      </div>
    </aside>
  );
};
