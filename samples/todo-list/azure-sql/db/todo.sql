drop table if exists [dbo].[todos]
create table [dbo].[todos]
(
	[id] uniqueidentifier  primary key nonclustered not null default(newid()),
	[title] [nvarchar](1000) not null,
	[completed] [bit] not null default (0),
	[owner_id] varchar(128) collate Latin1_General_100_BIN2 not null default ('public')
)
go

insert into dbo.todos
	(title, completed)
values
	('item-001', 0),
	('item-002', 1),
	('item-003', 1),
	('item-004', 0),
	('item-005', 1),
	('item-006', 0),
	('item-007', 0),
	('item-008', 1),
	('item-009', 1),
	('item-010', 0),
	('item-011', 1),
	('item-012', 0),
	('item-013', 1),
	('item-015', 1),
	('item-016', 0),
	('item-017', 0),
	('item-018', 1),
	('item-019', 1),
	('item-019', 1),
	('item-020', 0)
go
