DROP TABLE IF EXISTS [dbo].[todo];
GO
CREATE TABLE [dbo].[todo](
	[id] [int] NOT NULL IDENTITY,
	[title] [nvarchar](100) NOT NULL,
	[completed] [int] NOT NULL,
	[owner] [nvarchar](100) NOT NULL
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[todo] ADD PRIMARY KEY CLUSTERED 
(
	[id] ASC
)
ALTER TABLE [dbo].[todo] ADD  DEFAULT ((0)) FOR [completed]
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