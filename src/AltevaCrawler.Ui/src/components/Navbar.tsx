import React, { useState } from 'react';
import { Network, History, PlusCircle, Search } from 'lucide-react';

interface NavbarProps {
  activeTab: 'start' | 'details' | 'history';
  setActiveTab: (tab: 'start' | 'details' | 'history') => void;
  selectedJobId: string | null;
  onGlobalSearch: (query: string) => void;
}

export const Navbar: React.FC<NavbarProps> = ({
  activeTab,
  setActiveTab,
  selectedJobId,
  onGlobalSearch
}) => {
  const [query, setQuery] = useState('');

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (query.trim()) {
      onGlobalSearch(query.trim());
    }
  };

  return (
    <nav className="bg-slate-800 border-b border-slate-700 px-6 py-3 flex items-center justify-between shadow-lg flex-shrink-0">
      <div className="flex items-center space-x-3 cursor-pointer" onClick={() => setActiveTab('start')}>
        <Network className="w-7 h-7 text-cyan-400" />
        <div>
          <h1 className="text-lg font-bold bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text text-transparent">
            Alteva Crawler
          </h1>
        </div>
      </div>

      <form onSubmit={handleSearchSubmit} className="flex-1 max-w-md mx-6">
        <div className="relative">
          <Search className="w-4 h-4 absolute left-3 top-2.5 text-slate-400" />
          <input
            type="text"
            placeholder="Global search across all crawled web pages..."
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            className="w-full bg-slate-900 border border-slate-700 rounded-lg pl-9 pr-4 py-1.5 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500"
          />
        </div>
      </form>

      <div className="flex space-x-2">
        <button
          onClick={() => setActiveTab('start')}
          className={`flex items-center space-x-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition ${
            activeTab === 'start'
              ? 'bg-cyan-600 text-white shadow-md'
              : 'text-slate-300 hover:bg-slate-700'
          }`}
        >
          <PlusCircle className="w-3.5 h-3.5" />
          <span>New Crawl</span>
        </button>

        {selectedJobId && (
          <button
            onClick={() => setActiveTab('details')}
            className={`flex items-center space-x-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition ${
              activeTab === 'details'
                ? 'bg-cyan-600 text-white shadow-md'
                : 'text-slate-300 hover:bg-slate-700'
            }`}
          >
            <Network className="w-3.5 h-3.5" />
            <span>Job Details</span>
          </button>
        )}

        <button
          onClick={() => setActiveTab('history')}
          className={`flex items-center space-x-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition ${
            activeTab === 'history'
              ? 'bg-cyan-600 text-white shadow-md'
              : 'text-slate-300 hover:bg-slate-700'
          }`}
        >
          <History className="w-3.5 h-3.5" />
          <span>History</span>
        </button>
      </div>
    </nav>
  );
};
