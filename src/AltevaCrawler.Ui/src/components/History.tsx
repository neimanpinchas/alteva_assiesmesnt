import React, { useEffect, useState } from 'react';
import { getJobHistory } from '../api';
import { JobSummary } from '../types';
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
      <div className="flex items-center justify-between">
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
        <div className="space-y-4">
          {jobs.map((j) => (
            <div
              key={j.id}
              onClick={() => onSelectJob(j.id)}
              className="bg-slate-800 p-5 rounded-xl border border-slate-700 hover:border-cyan-500 cursor-pointer transition shadow-md flex items-center justify-between"
            >
              <div className="space-y-1">
                <div className="flex items-center space-x-3">
                  <Globe className="w-5 h-5 text-cyan-400" />
                  <span className="font-semibold text-white">{j.url}</span>
                  <span className={`px-2.5 py-0.5 text-xs font-semibold rounded-full ${
                    j.status === 'Completed' ? 'bg-green-900/60 text-green-300' :
                    j.status === 'Running' ? 'bg-blue-900/60 text-blue-300 animate-pulse' :
                    j.status === 'Canceled' ? 'bg-amber-900/60 text-amber-300' :
                    'bg-slate-700 text-slate-300'
                  }`}>
                    {j.status}
                  </span>
                </div>
                <div className="flex items-center space-x-4 text-xs text-slate-400">
                  <span className="font-mono">ID: {j.id.slice(0, 8)}...</span>
                  <span>Depth: {j.maxDepth}</span>
                  <span>Pages: {j.totalPagesCrawled}</span>
                  <span className="flex items-center space-x-1 text-cyan-400 font-semibold">
                    <Link className="w-3.5 h-3.5" />
                    <span>Domain Ratio: {((j.domainLinkRatio || 0) * 100).toFixed(1)}%</span>
                  </span>
                  <span className="flex items-center space-x-1">
                    <Clock className="w-3.5 h-3.5" />
                    <span>{new Date(j.createdAt).toLocaleString()}</span>
                  </span>
                </div>
              </div>

              <ArrowRight className="w-5 h-5 text-slate-500" />
            </div>
          ))}
        </div>
      )}

      <div className="flex justify-between items-center pt-4">
        <button
          disabled={page <= 1}
          onClick={() => setPage(p => Math.max(1, p - 1))}
          className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-lg disabled:opacity-50 text-sm font-medium"
        >
          Previous
        </button>
        <span className="text-sm text-slate-400">Page {page}</span>
        <button
          disabled={page * 10 >= total}
          onClick={() => setPage(p => p + 1)}
          className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-lg disabled:opacity-50 text-sm font-medium"
        >
          Next
        </button>
      </div>
    </div>
  );
};
