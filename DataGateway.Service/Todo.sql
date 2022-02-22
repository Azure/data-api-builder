DROP TABLE IF EXISTS [dbo].[todo];
GO

CREATE TABLE [dbo].[todo](
	[id] [int] NOT NULL IDENTITY PRIMARY KEY CLUSTERED,
	[title] [nvarchar](100) NOT NULL,
	[completed] [int] NOT NULL DEFAULT(0),
	[owner] [nvarchar](100) NOT NULL
)
GO

INSERT INTO dbo.todo (title, completed, [owner]) VALUES
('item 1', 0, 'damauri'),
('item 2', 0, 'damauri'),
('item 3', 1, 'damauri'),
('item 4', 1, 'damauri'),
('item 5', 0, 'damauri'),
('item 6', 1, 'anonymous'),
('item 7', 0, 'jdean'),
('item 8', 1, 'jdoe')
GO