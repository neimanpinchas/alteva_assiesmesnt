import React, { useEffect, useState } from 'react';
import { Navbar } from './components/Navbar';
import { SidebarHistory } from './components/SidebarHistory';
import { StartCrawl } from './components/StartCrawl';
import { JobDetails } from './components/JobDetails';
import { History } from './components/History';
import { globalSearch, getPageContent } from './api';
import { PageSearchResultDto } from './types';
import { Search, FileText } from 'lucide-react';

export const App: React.FC = () => {
  const [activeTab, setActiveTab] = useState<'start' | 'details' | 'history'>('start');
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null);
  const [globalResults, setGlobalResults] = useState<PageSearchResultDto[] | null>(null);
  const [selectedGlobalPage, setSelectedGlobalPage] = useState<{ url: string; html: string } | null>(null);

  // Hash-based client routing so page reloads preserve active job
  useEffect(() => {
    const handleHashChange = () => {
      const hash = window.location.hash;
      if (hash.startsWith('#/job/')) {
        const jobId = hash.replace('#/job/', '');
        setSelectedJobId(jobId);
        setActiveTab('details');
      }
    };

    handleHashChange();
    window.addEventListener('hashchange', handleHashChange);
    return () => window.removeEventListener('hashchange', handleHashChange);
  }, []);

  const selectJob = (jobId: string) => {
    setSelectedJobId(jobId);
    setActiveTab('details');
    window.location.hash = `#/job/${jobId}`;
  };

  const handleGlobalSearch = async (query: string) => {
    try {
      const results = await globalSearch(query);
      setGlobalResults(results);
    } catch (err) {
      console.error(err);
    }
  };

  const openGlobalPagePreview = async (res: PageSearchResultDto) => {
    try {
      const content = await getPageContent(res.jobId || 'global', res.id);
      setSelectedGlobalPage({ url: res.url, html: content.html });
    } catch (err) {
      setSelectedGlobalPage({ url: res.url, html: 'Content not found on disk.' });
    }
  };

  return (
    <div className="min-h-screen bg-slate-900 text-slate-100 flex flex-col font-sans">
      <Navbar
        activeTab={activeTab}
        setActiveTab={setActiveTab}
        selectedJobId={selectedJobId}
        onGlobalSearch={handleGlobalSearch}
      />

      <div className="flex flex-1 overflow-hidden">
        {/* Left Sidebar History Navigation */}
        <SidebarHistory
          selectedJobId={selectedJobId}
          onSelectJob={selectJob}
          onNewCrawl={() => { setActiveTab('start'); window.location.hash = ''; }}
        />

        {/* Main Dashboard Content Area */}
        <main className="flex-1 overflow-y-auto p-6 bg-slate-900/50">
          {globalResults && (
            <div className="bg-slate-800 p-6 rounded-xl border border-slate-700 shadow-xl mb-6 space-y-4">
              <div className="flex items-center justify-between border-b border-slate-700 pb-3">
                <div className="flex items-center space-x-2">
                  <Search className="w-5 h-5 text-cyan-400" />
                  <h3 className="text-md font-bold text-white">Global Search Results ({globalResults.length})</h3>
                </div>
                <button onClick={() => setGlobalResults(null)} className="text-xs text-slate-400 hover:text-white">
                  Close Results
                </button>
              </div>

              <div className="space-y-2 max-h-64 overflow-y-auto">
                {globalResults.map((r) => (
                  <div
                    key={r.id}
                    onClick={() => openGlobalPagePreview(r)}
                    className="p-3 bg-slate-900 hover:bg-slate-700 cursor-pointer rounded-lg border border-slate-700 flex justify-between items-center text-sm transition"
                  >
                    <span className="text-slate-200 truncate font-medium">{r.url}</span>
                    <span className="text-xs font-mono bg-indigo-900/60 text-indigo-300 px-2 py-0.5 rounded">
                      Match Score: {r.hammingDistance}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {selectedGlobalPage && (
            <div className="bg-slate-800 p-6 rounded-xl border border-slate-700 shadow-xl mb-6 space-y-4">
              <div className="flex items-center justify-between border-b border-slate-700 pb-3">
                <div className="flex items-center space-x-2">
                  <FileText className="w-5 h-5 text-cyan-400" />
                  <h4 className="text-md font-bold text-white">{selectedGlobalPage.url}</h4>
                </div>
                <button onClick={() => setSelectedGlobalPage(null)} className="text-xs text-slate-400 hover:text-white">
                  Close Preview
                </button>
              </div>
              <div
                className="p-4 bg-slate-900 rounded-lg text-xs font-mono text-slate-300 max-h-64 overflow-auto whitespace-pre-wrap border border-slate-700"
                dangerouslySetInnerHTML={{ __html: selectedGlobalPage.html }}
              />
            </div>
          )}

          {activeTab === 'start' && (
            <StartCrawl onJobCreated={selectJob} />
          )}

          {activeTab === 'details' && selectedJobId && (
            <JobDetails jobId={selectedJobId} onJobDeleted={() => { setSelectedJobId(null); setActiveTab('start'); }} />
          )}

          {activeTab === 'history' && (
            <History onSelectJob={selectJob} />
          )}
        </main>
      </div>
    </div>
  );
};

export default App;
