IF SCHEMA_ID('s005') IS NULL BEGIN
	EXEC('CREATE SCHEMA [s005] AUTHORIZATION [dbo]')
END
GO

/*
--ALTER USER [hawaii] WITH DEFAULT_SCHEMA = dbo;
--ALTER USER [hawaii] WITH DEFAULT_SCHEMA = s005;
*/

DROP TABLE IF EXISTS [s005].[users_todos];
DROP TABLE IF EXISTS [s005].[todos];
DROP TABLE IF EXISTS [s005].[users];
DROP TABLE IF EXISTS [s005].[categories];
GO

DROP SEQUENCE IF EXISTS [s005].[global_id]
CREATE SEQUENCE [s005].[global_id] 
AS INT START WITH 1 INCREMENT BY 1
GO

CREATE TABLE [s005].[categories]
(
	[id] [int] NOT NULL PRIMARY KEY DEFAULT(NEXT VALUE FOR [s005].[global_id]),
	[category] [nvarchar](100) NOT NULL,
)
GO
CREATE TABLE [s005].[users]
(
	[id] [int] NOT NULL PRIMARY KEY DEFAULT(NEXT VALUE FOR [s005].[global_id]),
	[first_name] [nvarchar](100) NOT NULL,
	[middle_name] [int] NULL,
	[last_name] [nvarchar](100) NOT NULL,
	[email] [nvarchar](1000) NOT NULL
)
GO
CREATE TABLE [s005].[todos]
(
	[id] [int] NOT NULL PRIMARY KEY DEFAULT(NEXT VALUE FOR [s005].[global_id]),
	[title] [nvarchar](100) NOT NULL,
	[completed] [int] NOT NULL DEFAULT(0),
    [category_id] [int] NULL FOREIGN KEY REFERENCES s005.categories(id),
	[owner_id] [int] NOT NULL FOREIGN KEY REFERENCES s005.users(id),
	[last_updated] DATETIME2 NOT NULL DEFAULT(SYSDATETIME())
)
GO
CREATE TABLE [s005].[users_todos]
(
	[id] [int] NOT NULL PRIMARY KEY DEFAULT(NEXT VALUE FOR [s005].[global_id]),
	[user_id] [int] NOT NULL FOREIGN KEY REFERENCES s005.users(id),
	[todo_id] [int] NOT NULL FOREIGN KEY REFERENCES s005.todos(id),	
)
GO

INSERT s005.users 
    (id, first_name, middle_name, last_name, email) 
VALUES 
    (1, 'Nancy', NULL, 'Davolio', 'nadav@northwind.com'),
    (2, 'Andrew', NULL, 'Fuller', 'anfu@northwind.com'),
    (3, 'Janet', NULL, 'Leverling', 'jale@northwind.com'),
    (4, 'Margaret', NULL, 'Peacock', 'mape@northwind.com')
go

INSERT INTO s005.categories 
    (id, category)
VALUES
    (200, 'cat-a'),
    (201, 'cat-b'),
    (202, 'cat-c')
GO

INSERT INTO s005.todos
    (id, title, completed, category_id, owner_id)
VALUES
    (100, 'todo-001', 0, null, 1),
    (101, 'todo-002', 0, 200, 1),
    (102, 'todo-003', 1, 200, 2),
    (103, 'todo-004', 0, 201, 2),
    (104, 'todo-005', 1, 202, 3),
    (105, 'todo-006', 1, null, 3),
    (106, 'todo-007', 0, null, 4),
    (107, 'todo-008', 1, 202, 4)
GO

INSERT INTO s005.users_todos
    (user_id, todo_id)
VALUES
    (1, 101),
    (1, 103),
    (2, 104),
    (2, 105)
go

ALTER SEQUENCE [s005].[global_id] 
RESTART WITH 1000 INCREMENT BY 1
GO
