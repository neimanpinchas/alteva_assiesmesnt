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