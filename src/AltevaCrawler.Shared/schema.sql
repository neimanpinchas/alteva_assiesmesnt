-- this file has been created using the legoconv db converter tool by user PSN on new mint
CREATE TABLE [Jobs] (
    [Id] nvarchar(max) NOT NULL PRIMARY KEY,
    [Url] nvarchar(max) NOT NULL,
    [MaxDepth] nvarchar(max) NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    [TotalPagesCrawled] nvarchar(max) NOT NULL,
    [TotalBytesDownloaded] BIGINT NOT NULL,
    [CreatedAt] datetime NOT NULL,
    [StartedAt] datetime NULL,
    [CompletedAt] datetime NULL,
    [FailureReason] NVARCHAR(512) NULL
);

CREATE NONCLUSTERED INDEX [IX_Jobs_Status] ON [Jobs] ([Status]);

CREATE NONCLUSTERED INDEX [IX_Jobs_CreatedAt] ON [Jobs] ([CreatedAt]);

CREATE TABLE [Pages] (
    [Id] BIGINT NOT NULL IDENTITY(1,1) PRIMARY KEY,
    [Url] NVARCHAR(2048) NOT NULL,
    [NormalizedUrl] NVARCHAR(2048) NOT NULL,
    [UrlCrc32] INT NOT NULL,
    [DomainHash] INT NOT NULL,
    [Domain] NVARCHAR(255) NOT NULL,
    [SimHash] BIGINT NOT NULL,
    [ContentHash] NVARCHAR(64) NULL,
    [StoragePath] NVARCHAR(512) NULL,
    [ByteSize] BIGINT NOT NULL,
    [StatusCode] INT NOT NULL,
    [OutgoingLinksCount] INT NOT NULL,
    [IsCrawled] BIT NOT NULL,
    [CrawledAt] datetime NULL
);

CREATE NONCLUSTERED INDEX [IX_Pages_UrlCrc32] ON [Pages] ([UrlCrc32]);

CREATE NONCLUSTERED INDEX [IX_Pages_DomainHash] ON [Pages] ([DomainHash]);

CREATE NONCLUSTERED INDEX [IX_Pages_IsCrawled] ON [Pages] ([IsCrawled]);

CREATE TABLE [JobPageLinks] (
    [Id] BIGINT NOT NULL IDENTITY(1,1) PRIMARY KEY,
    [JobId] NVARCHAR(36) NOT NULL,
    [SourcePageId] BIGINT NOT NULL,
    [TargetPageId] BIGINT NOT NULL,
    [CreatedAt] datetime NOT NULL
);

CREATE NONCLUSTERED INDEX [IX_JobPageLinks_Job_Source] ON [JobPageLinks] ([JobId], [SourcePageId]);

CREATE NONCLUSTERED INDEX [IX_JobPageLinks_Job_Target] ON [JobPageLinks] ([JobId], [TargetPageId]);

CREATE TABLE [JobStatusTransitions] (
    [Id] BIGINT NOT NULL IDENTITY(1,1) PRIMARY KEY,
    [JobId] NVARCHAR(36) NOT NULL,
    [FromStatus] INT NOT NULL,
    [ToStatus] INT NOT NULL,
    [TransitionedAt] datetime NOT NULL,
    [Reason] NVARCHAR(255) NULL
);

CREATE NONCLUSTERED INDEX [IX_JobTransitions_JobId] ON [JobStatusTransitions] ([JobId]);
