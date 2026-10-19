import React, { useState } from 'react';
import { createJob } from '../api';
import { Play, Globe, Layers, AlertCircle, HardDrive } from 'lucide-react';

interface StartCrawlProps {
  onJobCreated: (jobId: string) => void;
}

export const StartCrawl: React.FC<StartCrawlProps> = ({ onJobCreated }) => {
  const [url, setUrl] = useState('');
  const [maxDepth, setMaxDepth] = useState(2);
  const [maxPagesLimit, setMaxPagesLimit] = useState(200);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!url.trim()) return;

    setLoading(true);
    setError(null);

    let formattedUrl = url.trim();
    if (!formattedUrl.startsWith('http://') && !formattedUrl.startsWith('https://')) {
      formattedUrl = 'https://' + formattedUrl;
    }

    try {
      const res = await createJob(formattedUrl, maxDepth, maxPagesLimit);
      onJobCreated(res.jobId);
    } catch (err: any) {
      setError(err.message || 'Failed to submit crawl job.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-2xl mx-auto my-12 bg-slate-800 p-8 rounded-xl border border-slate-700 shadow-2xl">
      <div className="flex items-center space-x-3 mb-6">
        <Globe className="w-8 h-8 text-cyan-400" />
        <div>
          <h2 className="text-2xl font-bold text-white">Start New Web Crawl</h2>
          <p className="text-slate-400 text-sm">Enter a website URL (e.g. <span className="text-cyan-300 font-mono">auto.com</span> or <span className="text-cyan-300 font-mono">https://example.com</span>) to start crawling.</p>
        </div>
      </div>

      {error && (
        <div className="mb-6 p-4 bg-red-900/50 border border-red-700 rounded-lg flex items-center space-x-3 text-red-200 text-sm">
          <AlertCircle className="w-5 h-5 text-red-400 flex-shrink-0" />
          <span>{error}</span>
        </div>
      )}

      <form onSubmit={handleSubmit} className="space-y-6">
        <div>
          <label className="block text-sm font-medium text-slate-300 mb-2">Target Website URL</label>
          <div className="relative">
            <Globe className="w-5 h-5 absolute left-3 top-3 text-slate-500" />
            <input
              type="text"
              required
              placeholder="auto.com or https://example.com"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              className="w-full bg-slate-900 border border-slate-700 rounded-lg pl-10 pr-4 py-3 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 focus:ring-1 focus:ring-cyan-500 transition"
            />
          </div>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <div className="flex justify-between items-center mb-2">
              <label className="block text-sm font-medium text-slate-300">Depth Limit</label>
              <span className="text-sm font-semibold text-cyan-400">Depth {maxDepth}</span>
            </div>
            <div className="flex items-center space-x-3">
              <Layers className="w-5 h-5 text-slate-500" />
              <input
                type="range"
                min="1"
                max="5"
                value={maxDepth}
                onChange={(e) => setMaxDepth(parseInt(e.target.value))}
                className="w-full accent-cyan-500 cursor-pointer"
              />
            </div>
          </div>

          <div>
            <div className="flex justify-between items-center mb-2">
              <label className="block text-sm font-medium text-slate-300">Max Page Safety Limit</label>
              <span className="text-sm font-semibold text-cyan-400">{maxPagesLimit} Pages</span>
            </div>
            <div className="flex items-center space-x-3">
              <HardDrive className="w-5 h-5 text-slate-500" />
              <input
                type="number"
                min="10"
                max="1000"
                value={maxPagesLimit}
                onChange={(e) => setMaxPagesLimit(parseInt(e.target.value))}
                className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-white font-mono text-sm focus:outline-none focus:border-cyan-500"
              />
            </div>
          </div>
        </div>

        <button
          type="submit"
          disabled={loading}
          className="w-full py-3 px-6 bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-400 hover:to-blue-500 text-white font-semibold rounded-lg shadow-lg flex items-center justify-center space-x-2 transition disabled:opacity-50"
        >
          {loading ? (
            <div className="w-5 h-5 border-2 border-white border-t-transparent rounded-full animate-spin" />
          ) : (
            <>
              <Play className="w-5 h-5" />
              <span>Start Crawling Job</span>
            </>
          )}
        </button>
      </form>
    </div>
  );
};
