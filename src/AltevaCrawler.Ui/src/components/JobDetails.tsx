import React, { useEffect, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import Tree from 'rc-tree';
import {
  ComposedChart, Area, Line, XAxis, YAxis, Tooltip, ResponsiveContainer,
  PieChart, Pie, Cell, BarChart, Bar
} from 'recharts';
import {
  getJob, getJobTree, getJobMetrics, searchJobPages, getPageContent,
  pauseJob, resumeJob, cancelJob, deleteJob
} from '../api';
import { JobSummary, TreeNodeDto, JobMetricsDto, PageSearchResultDto, getJobStatusBadgeClass } from '../types';
import {
  Activity, Globe, Search, Layers, Ban, FileText,
  Clock, Pause, Play, Trash2
} from 'lucide-react';

interface JobDetailsProps {
  jobId: string;
  onJobDeleted?: () => void;
}

export const JobDetails: React.FC<JobDetailsProps> = ({ jobId, onJobDeleted }) => {
  const [activeTab, setActiveTab] = useState<'status' | 'tree' | 'search'>('status');
  const [job, setJob] = useState<JobSummary | null>(null);
  const [metrics, setMetrics] = useState<JobMetricsDto | null>(null);
  const [treeData, setTreeData] = useState<any[]>([]);
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<PageSearchResultDto[]>([]);
  const [selectedPageContent, setSelectedPageContent] = useState<string | null>(null);
  const [selectedPageUrl, setSelectedPageUrl] = useState<string | null>(null);
  const [progressHistory, setProgressHistory] = useState<any[]>([]);

  useEffect(() => {
    loadJobDetails();
    loadTree();
    loadMetrics();

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/crawlProgress')
      .withAutomaticReconnect()
      .build();

    connection.start().then(() => {
      connection.invoke('JoinJobGroup', jobId);
    }).catch(console.error);

    connection.on('PageCrawled', (data) => {
      setJob(prev => prev ? {
        ...prev,
        totalPagesCrawled: prev.totalPagesCrawled + 1,
        totalBytesDownloaded: prev.totalBytesDownloaded + data.byteSize
      } : prev);

      setProgressHistory(prev => {
        const last = prev[prev.length - 1] || { pages: 0, bytes: 0 };
        const newPages = last.pages + 1;
        const newBytes = last.bytes + data.byteSize;
        return [
          ...prev,
          {
            time: new Date().toLocaleTimeString(),
            pages: newPages,
            bytes: newBytes,
            forecastPages: newPages,
            forecastBytes: newBytes
          }
        ];
      });
    });

    connection.on('JobProgressUpdated', (data) => {
      setJob(prev => prev ? {
        ...prev,
        status: data.status,
        totalPagesCrawled: data.crawledPagesCount,
        totalBytesDownloaded: data.totalBytesDownloaded
      } : prev);
      loadTree();
      loadMetrics();
    });

    return () => {
      connection.stop();
    };
  }, [jobId]);

  const loadJobDetails = async () => {
    try {
      const data = await getJob(jobId);
      setJob(data);
    } catch (err) {
      console.error(err);
    }
  };

  const loadMetrics = async () => {
    try {
      const data = await getJobMetrics(jobId);
      setMetrics(data);
    } catch (err) {
      console.error(err);
    }
  };

  const loadTree = async () => {
    try {
      const nodes = await getJobTree(jobId, undefined, 3);
      setTreeData(mapToRcTree(nodes));
    } catch (err) {
      console.error(err);
    }
  };

  const mapToRcTree = (nodes: TreeNodeDto[]): any[] => {
    return nodes.map(n => ({
      key: n.id,
      title: (
        <div className="flex items-center space-x-2 text-sm">
          <span className="font-semibold text-cyan-300">{n.url}</span>
          <span className="px-2 py-0.5 text-xs bg-slate-700 rounded text-slate-300">
            Domain Ratio: {(n.onTheFlyDomainLinkRatio * 100).toFixed(1)}%
          </span>
          <span className="px-2 py-0.5 text-xs bg-indigo-900/60 text-indigo-300 rounded font-mono" title={`SimHash Fingerprint: ${n.simHashHex}`}>
            Signature
          </span>
        </div>
      ),
      rawNode: n,
      children: n.children && n.children.length > 0 ? mapToRcTree(n.children) : []
    }));
  };

  const handleSelectNode = async (_selectedKeys: any, info: any) => {
    if (info.node && info.node.rawNode) {
      const page = info.node.rawNode as TreeNodeDto;
      openPagePreview(page.id, page.url);
    }
  };

  const openPagePreview = async (pageId: number, url: string) => {
    setSelectedPageUrl(url);
    try {
      const res = await getPageContent(jobId, pageId);
      let html = res.html;

      // Highlight matching query keywords in text
      if (searchQuery.trim()) {
        const regex = new RegExp(`(${searchQuery.trim().replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi');
        html = html.replace(regex, '<mark class="bg-yellow-400 text-black px-1 rounded">$1</mark>');
      }

      setSelectedPageContent(html);
    } catch (err) {
      setSelectedPageContent('Page content HTML file not found or not crawled yet.');
    }
  };

  const handleSearch = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!searchQuery) return;
    try {
      const results = await searchJobPages(jobId, searchQuery);
      setSearchResults(results);
    } catch (err) {
      console.error(err);
    }
  };

  const handlePause = async () => {
    try {
      await pauseJob(jobId);
      loadJobDetails();
    } catch (err) { console.error(err); }
  };

  const handleResume = async () => {
    try {
      await resumeJob(jobId);
      loadJobDetails();
    } catch (err) { console.error(err); }
  };

  const handleCancel = async () => {
    try {
      await cancelJob(jobId);
      loadJobDetails();
    } catch (err) { console.error(err); }
  };

  const handleDelete = async () => {
    if (!confirm('Are you sure you want to delete this job and prune site pages?')) return;
    try {
      await deleteJob(jobId);
      if (onJobDeleted) onJobDeleted();
    } catch (err) { console.error(err); }
  };

  if (!job) {
    return (
      <div className="flex justify-center items-center py-24">
        <div className="w-10 h-10 border-4 border-cyan-500 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  // Smooth 2D forecast curve extending current yellow mountain
  const lastHistory = progressHistory[progressHistory.length - 1] || { pages: job.totalPagesCrawled, bytes: job.totalBytesDownloaded };
  const chartData = progressHistory.length > 0 ? [
    ...progressHistory,
    { time: 'Forecast End', pages: null, bytes: null, forecastPages: lastHistory.pages + 15, forecastBytes: lastHistory.bytes + 150000 }
  ] : [
    { time: '0s', pages: 0, bytes: 0, forecastPages: 0, forecastBytes: 0 },
    { time: 'Current', pages: job.totalPagesCrawled, bytes: job.totalBytesDownloaded, forecastPages: job.totalPagesCrawled, forecastBytes: job.totalBytesDownloaded },
    { time: 'Forecast End', pages: null, bytes: null, forecastPages: job.totalPagesCrawled + 15, forecastBytes: job.totalBytesDownloaded + 150000 }
  ];

  const pieData = metrics ? [
    { name: 'Internal Links', value: metrics.domainLinkRatioPie.internalLinks, color: '#06b6d4' },
    { name: 'External Links', value: metrics.domainLinkRatioPie.externalLinks, color: '#f59e0b' }
  ] : [];

  const barData = metrics ? [
    { name: 'Unique to Job', count: metrics.pageSharing.uniqueToThisJob },
    { name: 'Shared Across Jobs', count: metrics.pageSharing.sharedAcrossJobs }
  ] : [];

  return (
    <div className="max-w-7xl mx-auto px-6 py-8 space-y-6">
      {/* Header Banner */}
      <div className="bg-slate-800 p-6 rounded-xl border border-slate-700 shadow-xl flex flex-wrap items-center justify-between gap-4">
        <div className="space-y-1">
          <div className="flex items-center space-x-3">
            <Globe className="w-6 h-6 text-cyan-400" />
            <h2 className="text-xl font-bold text-white">{job.url}</h2>
            <span className={`px-3 py-1 text-xs font-semibold rounded-full ${getJobStatusBadgeClass(job.status)}`}>
              {job.status}
            </span>
          </div>
          <p className="text-xs text-slate-400 font-mono">Job ID: {job.id}</p>
        </div>

        <div className="flex items-center space-x-4">
          <div className="text-right">
            <p className="text-xs text-slate-400">Pages Crawled</p>
            <p className="text-lg font-bold text-cyan-400">{job.totalPagesCrawled} / {job.maxPagesLimit}</p>
          </div>
          <div className="text-right">
            <p className="text-xs text-slate-400">Bytes Downloaded</p>
            <p className="text-lg font-bold text-blue-400">{(job.totalBytesDownloaded / 1024).toFixed(1)} KB</p>
          </div>

          <div className="flex items-center space-x-2 pl-4 border-l border-slate-700">
            {job.status === 'Running' ? (
              <button onClick={handlePause} title="Pause Crawl" className="p-2 bg-amber-600 hover:bg-amber-500 text-white rounded-lg transition">
                <Pause className="w-4 h-4" />
              </button>
            ) : job.status === 'Pending' ? (
              <button onClick={handleResume} title="Resume Crawl" className="p-2 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg transition">
                <Play className="w-4 h-4" />
              </button>
            ) : null}

            {job.status === 'Running' && (
              <button onClick={handleCancel} title="Cancel Job" className="p-2 bg-rose-600 hover:bg-rose-500 text-white rounded-lg transition">
                <Ban className="w-4 h-4" />
              </button>
            )}

            <button onClick={handleDelete} title="Delete & Prune Job" className="p-2 bg-slate-700 hover:bg-red-700 text-slate-300 hover:text-white rounded-lg transition">
              <Trash2 className="w-4 h-4" />
            </button>
          </div>
        </div>
      </div>

      {/* Tab Navigation */}
      <div className="flex border-b border-slate-700 space-x-4">
        <button
          onClick={() => setActiveTab('status')}
          className={`pb-3 px-2 text-sm font-semibold transition border-b-2 flex items-center space-x-2 ${
            activeTab === 'status'
              ? 'border-cyan-400 text-cyan-400'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Activity className="w-4 h-4" />
          <span>1. Crawl Status & Metrics</span>
        </button>

        <button
          onClick={() => setActiveTab('tree')}
          className={`pb-3 px-2 text-sm font-semibold transition border-b-2 flex items-center space-x-2 ${
            activeTab === 'tree'
              ? 'border-cyan-400 text-cyan-400'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Layers className="w-4 h-4" />
          <span>2. Link Tree View</span>
        </button>

        <button
          onClick={() => setActiveTab('search')}
          className={`pb-3 px-2 text-sm font-semibold transition border-b-2 flex items-center space-x-2 ${
            activeTab === 'search'
              ? 'border-cyan-400 text-cyan-400'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Search className="w-4 h-4" />
          <span>3. Fast FreeText Search</span>
        </button>
      </div>

      {/* TAB 1: CRAWL STATUS & METRICS */}
      {activeTab === 'status' && (
        <div className="space-y-6">
          <div className="bg-slate-800 p-6 rounded-xl border border-slate-700 shadow-xl space-y-4">
            <div className="flex items-center justify-between">
              <div className="flex items-center space-x-2">
                <Activity className="w-5 h-5 text-amber-400" />
                <h3 className="text-lg font-semibold text-white">2D Crawl Progress & Smooth Forecast</h3>
              </div>
              {metrics?.forecastedCompletionTime && (
                <div className="flex items-center space-x-2 bg-slate-900 px-3 py-1.5 rounded-lg border border-slate-700 text-xs text-amber-300">
                  <Clock className="w-4 h-4" />
                  <span>Est. Completion: {new Date(metrics.forecastedCompletionTime).toLocaleTimeString()}</span>
                </div>
              )}
            </div>

            <div className="h-64 w-full">
              <ResponsiveContainer width="100%" height="100%">
                <ComposedChart data={chartData}>
                  <XAxis dataKey="time" stroke="#94a3b8" />
                  <YAxis yAxisId="left" stroke="#eab308" label={{ value: 'Pages', angle: -90, position: 'insideLeft', fill: '#eab308' }} />
                  <YAxis yAxisId="right" orientation="right" stroke="#3b82f6" label={{ value: 'Bytes', angle: 90, position: 'insideRight', fill: '#3b82f6' }} />
                  <Tooltip contentStyle={{ backgroundColor: '#1e293b', borderColor: '#475569' }} />
                  <Area yAxisId="left" type="monotone" dataKey="pages" stroke="#eab308" fill="#eab308" fillOpacity={0.4} name="Crawled Pages" />
                  <Line yAxisId="right" type="monotone" dataKey="bytes" stroke="#3b82f6" strokeWidth={2} name="Bytes Downloaded" />
                  <Area yAxisId="left" type="monotone" dataKey="forecastPages" stroke="#fef08a" fill="#fef08a" fillOpacity={0.15} strokeDasharray="3 3" name="Forecast Continuation" />
                </ComposedChart>
              </ResponsiveContainer>
            </div>
          </div>

          {metrics && (
            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
              <div className="bg-slate-800 p-6 rounded-xl border border-slate-700 shadow-xl space-y-3">
                <h4 className="text-sm font-semibold text-slate-300">Domain Link Ratio (Pie Chart)</h4>
                <div className="h-48">
                  <ResponsiveContainer width="100%" height="100%">
                    <PieChart>
                      <Pie data={pieData} dataKey="value" nameKey="name" cx="50%" cy="50%" outerRadius={60} label>
                        {pieData.map((entry, index) => (
                          <Cell key={`cell-${index}`} fill={entry.color} />
                        ))}
                      </Pie>
                      <Tooltip contentStyle={{ backgroundColor: '#1e293b' }} />
                    </PieChart>
                  </ResponsiveContainer>
                </div>
                <p className="text-center text-xs text-cyan-400 font-semibold">
                  Internal Domain Ratio: {(metrics.domainLinkRatioPie.domainLinkRatio * 100).toFixed(1)}%
                </p>
              </div>

              <div className="bg-slate-800 p-6 rounded-xl border border-slate-700 shadow-xl space-y-3">
                <h4 className="text-sm font-semibold text-slate-300">Unique vs Shared Pages</h4>
                <div className="h-48">
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={barData}>
                      <XAxis dataKey="name" stroke="#94a3b8" fontSize={11} />
                      <YAxis stroke="#94a3b8" />
                      <Tooltip contentStyle={{ backgroundColor: '#1e293b' }} />
                      <Bar dataKey="count" fill="#8b5cf6" />
                    </BarChart>
                  </ResponsiveContainer>
                </div>
              </div>

              <div className="bg-slate-800 p-6 rounded-xl border border-slate-700 shadow-xl space-y-4 flex flex-col justify-between">
                <h4 className="text-sm font-semibold text-slate-300">Download Shortcuts & Efficiency</h4>
                <div className="space-y-4">
                  <div className="p-4 bg-slate-900 rounded-lg border border-slate-700">
                    <p className="text-xs text-slate-400">Downloaded Bandwidth</p>
                    <p className="text-xl font-bold text-blue-400">{(metrics.bandwidthEfficiency.totalBytesDownloaded / 1024).toFixed(1)} KB</p>
                  </div>
                  <div className="p-4 bg-slate-900 rounded-lg border border-slate-700">
                    <p className="text-xs text-slate-400">Saved Bandwidth (Shortcuts)</p>
                    <p className="text-xl font-bold text-green-400">{(metrics.bandwidthEfficiency.estimatedBytesSaved / 1024).toFixed(1)} KB</p>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>
      )}

      {/* TAB 2: LINK TREE VIEW */}
      {activeTab === 'tree' && (
        <div className="bg-slate-800 p-6 rounded-xl border border-slate-700 shadow-xl space-y-4">
          <div className="flex items-center space-x-3">
            <Layers className="w-5 h-5 text-cyan-400" />
            <h3 className="text-lg font-semibold text-white">Hierarchical Page Graph Tree View</h3>
          </div>
          <p className="text-xs text-slate-400">Click any tree node to inspect page metadata, domain link ratio, and raw stored disk HTML.</p>

          {treeData.length > 0 ? (
            <div className="p-4 bg-slate-900 rounded-lg border border-slate-700 max-h-96 overflow-auto">
              <Tree
                treeData={treeData}
                onSelect={handleSelectNode}
                defaultExpandAll
              />
            </div>
          ) : (
            <p className="text-slate-500 text-sm">No page tree discovered yet or job is pending...</p>
          )}
        </div>
      )}

      {/* TAB 3: FAST FREETEXT SEARCH WITH SIDEBAR */}
      {activeTab === 'search' && (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          <div className="md:col-span-2 bg-slate-800 p-6 rounded-xl border border-slate-700 shadow-xl space-y-4">
            <div className="flex items-center space-x-3">
              <Search className="w-5 h-5 text-cyan-400" />
              <h3 className="text-lg font-semibold text-white">FreeText Keyword Search</h3>
            </div>
            <form onSubmit={handleSearch} className="flex space-x-3">
              <input
                type="text"
                placeholder="Type keywords to search crawled documents..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="flex-1 bg-slate-900 border border-slate-700 rounded-lg px-4 py-2 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500"
              />
              <button type="submit" className="px-5 py-2 bg-cyan-600 hover:bg-cyan-500 text-white font-medium rounded-lg transition">
                Search
              </button>
            </form>

            {searchResults.length > 0 && (
              <div className="space-y-2 mt-4 max-h-80 overflow-y-auto">
                {searchResults.map((res) => (
                  <div
                    key={res.id}
                    onClick={() => openPagePreview(res.id, res.url)}
                    className="p-3 bg-slate-900 hover:bg-slate-700 cursor-pointer rounded-lg border border-slate-700 flex justify-between items-center text-sm transition"
                  >
                    <span className="text-slate-200 truncate max-w-md font-medium">{res.url}</span>
                    <span className="px-2 py-1 bg-indigo-900/60 text-indigo-300 text-xs font-mono rounded" title={`Similarity Match Distance: ${res.hammingDistance}`}>
                      Relevance Match
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Sidebar listing searched files */}
          <div className="bg-slate-800 p-6 rounded-xl border border-slate-700 shadow-xl space-y-3">
            <div className="flex items-center space-x-2 text-slate-300 font-semibold border-b border-slate-700 pb-2">
              <FileText className="w-4 h-4 text-cyan-400" />
              <span>Searched Files Sidebar</span>
            </div>
            {searchResults.length > 0 ? (
              <div className="space-y-2 max-h-80 overflow-y-auto">
                {searchResults.map((res) => (
                  <div
                    key={res.id}
                    onClick={() => openPagePreview(res.id, res.url)}
                    className="p-2 bg-slate-900 hover:bg-slate-700 cursor-pointer rounded text-xs text-slate-300 truncate transition border border-slate-700/50"
                  >
                    📄 {res.domain} / Page #{res.id}
                  </div>
                ))}
              </div>
            ) : (
              <p className="text-xs text-slate-500">Perform a search to populate the sidebar file list.</p>
            )}
          </div>
        </div>
      )}

      {/* Page Content Preview Drawer with Keyword Highlighting */}
      {selectedPageUrl && (
        <div className="bg-slate-800 p-6 rounded-xl border border-slate-700 shadow-xl space-y-4">
          <div className="flex items-center justify-between border-b border-slate-700 pb-3">
            <div className="flex items-center space-x-2">
              <FileText className="w-5 h-5 text-cyan-400" />
              <h4 className="text-md font-bold text-white truncate max-w-xl">{selectedPageUrl}</h4>
            </div>
            <button
              onClick={() => { setSelectedPageUrl(null); setSelectedPageContent(null); }}
              className="text-slate-400 hover:text-white text-sm font-semibold"
            >
              Close Preview
            </button>
          </div>
          <div
            className="p-4 bg-slate-900 rounded-lg text-xs font-mono text-slate-300 max-h-72 overflow-auto whitespace-pre-wrap border border-slate-700"
            dangerouslySetInnerHTML={{ __html: selectedPageContent || 'Loading content...' }}
          />
        </div>
      )}
    </div>
  );
};
