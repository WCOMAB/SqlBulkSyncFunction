-- Idempotent demo schema/data for the SyncTest job (source + target in one DB).
IF DB_ID('SyncTest') IS NULL
BEGIN
  CREATE DATABASE [SyncTest];
END
GO
ALTER DATABASE [SyncTest]
SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 7 DAYS, AUTO_CLEANUP = ON)
GO
USE [SyncTest]
GO
IF SCHEMA_ID('source') IS NULL EXEC('CREATE SCHEMA [source]')
GO
IF OBJECT_ID('source.Test') IS NULL
CREATE TABLE [source].[Test](
  [Id] [bigint] IDENTITY(1,1) NOT NULL PRIMARY KEY,
  [Description] [nvarchar](256) NULL,
  [Created] [datetime] NOT NULL
)
GO
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID('source.Test'))
ALTER TABLE [source].[Test] ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON)
GO
IF NOT EXISTS (SELECT 1 FROM source.[Test])
BEGIN
  INSERT INTO source.[Test] (Description, Created) VALUES ('Row 1', GETDATE());
  INSERT INTO source.[Test] (Description, Created) VALUES ('Row 2', GETDATE());
END
GO
IF SCHEMA_ID('target') IS NULL EXEC('CREATE SCHEMA [target]')
GO
IF OBJECT_ID('target.Test') IS NULL
CREATE TABLE [target].[Test](
  [Id] [bigint] IDENTITY(1,1) NOT NULL PRIMARY KEY,
  [Description] [nvarchar](256) NULL,
  [Created] [datetime] NOT NULL
)
GO
