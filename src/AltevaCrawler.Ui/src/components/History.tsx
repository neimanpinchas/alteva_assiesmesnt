import React, { useEffect, useState } from 'react';
import { getJobHistory } from '../api';
import { JobSummary, getJobStatusBadgeClass } from '../types';
import { History as HistoryIcon, ArrowRight, Clock, Globe, Link } from 'lucide-react';

interface HistoryProps {
  onSelectJob: (jobId: string) => void;
}

export const History: React.FC<HistoryProps> = ({ onSelectJob }) => {
  const [jobs, setJobs] = useState<JobSummary[]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    loadHistory();
  }, [page]);

  const loadHistory = async () => {
    setLoading(true);
    try {
      const res = await getJobHistory(page, 10);
      setJobs(res.items);
      setTotal(res.total);
    } catch (err) {
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-5xl mx-auto my-8 space-y-6">
      <div className="flex items-center justify-between border-b border-slate-700 pb-4">
        <div className="flex items-center space-x-3">
          <HistoryIcon className="w-7 h-7 text-cyan-400" />
          <h2 className="text-2xl font-bold text-white">Crawl Job History</h2>
        </div>
        <span className="text-sm text-slate-400">Total Jobs: {total}</span>
      </div>

      {loading ? (
        <div className="flex justify-center py-12">
          <div className="w-8 h-8 border-4 border-cyan-500 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : (
        <div className="divide-y divide-slate-700/60 bg-slate-800/80 rounded-xl border border-slate-700 shadow-xl overflow-hidden">
          {jobs.map((j) => (
            <div
              key={j.id}
              onClick={() => onSelectJob(j.id)}
              className="p-4 hover:bg-slate-700/50 cursor-pointer transition flex items-center justify-between"
            >
              <div className="space-y-1">
                <div className="flex items-center space-x-3">
                  <Globe className="w-4 h-4 text-cyan-400" />
                  <span className="font-semibold text-white text-sm">{j.url}</span>
                  <span className={`px-2 py-0.5 text-[11px] font-semibold rounded-full ${getJobStatusBadgeClass(j.status)}`}>
                    {j.status}
                  </span>
                </div>
                <div className="flex items-center space-x-4 text-xs text-slate-400">
                  <span className="font-mono">ID: {j.id.slice(0, 8)}...</span>
                  <span>Depth: {j.maxDepth}</span>
                  <span>Pages: {j.totalPagesCrawled}</span>
                  <span className="text-cyan-400 font-semibold flex items-center space-x-1">
                    <Link className="w-3 h-3" />
                    <span>Domain Ratio: {((j.domainLinkRatio || 0) * 100).toFixed(1)}%</span>
                  </span>
                  <span className="flex items-center space-x-1">
                    <Clock className="w-3 h-3" />
                    <span>{new Date(j.createdAt).toLocaleString()}</span>
                  </span>
                </div>
              </div>

              <ArrowRight className="w-4 h-4 text-slate-500" />
            </div>
          ))}
        </div>
      )}

      {/* Pagination Controls */}
      <div className="flex justify-between items-center pt-2">
        <button
          disabled={page <= 1}
          onClick={() => setPage(p => Math.max(1, p - 1))}
          className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-lg disabled:opacity-50 text-sm font-medium transition border border-slate-700"
        >
          Previous
        </button>
        <span className="text-sm text-slate-400 font-mono">Page {page} of {Math.ceil(total / 10) || 1}</span>
        <button
          disabled={page * 10 >= total}
          onClick={() => setPage(p => p + 1)}
          className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-lg disabled:opacity-50 text-sm font-medium transition border border-slate-700"
        >
          Next
        </button>
      </div>
    </div>
  );
};
