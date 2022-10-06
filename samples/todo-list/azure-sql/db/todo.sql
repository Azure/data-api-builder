drop table if exists [dbo].[todos]
drop table if exists [dbo].[categories]
go

create table [dbo].[categories]
(
	[id] uniqueidentifier  primary key nonclustered not null default(newid()),
	[category] [nvarchar](1000) not null,
)
go

create table [dbo].[todos]
(
	[id] uniqueidentifier  primary key nonclustered not null default(newid()),
	[category_id] uniqueidentifier null foreign key references [dbo].[categories](id),
	[todo] [nvarchar](1000) not null,
	[completed] [bit] not null default (0),
	[owner_id] varchar(128) collate Latin1_General_100_BIN2 not null default ('public')	
)
go

declare @cid uniqueidentifier = newid();

insert into [dbo].[categories]
	(id, category)
values
	(@cid, 'Test Category')

insert into [dbo].[todos]
	(todo, completed, category_id)
values
	('item-001', 0, null),
	('item-002', 1, null),
	('item-003', 1, @cid),
	('item-004', 0, @cid),
	('item-005', 1, @cid),
	('item-006', 0, null),
	('item-007', 0, null),
	('item-008', 1, @cid),
	('item-009', 1, @cid),
	('item-010', 0, @cid),
	('item-011', 1, null),
	('item-012', 0, @cid),
	('item-013', 1, null),
	('item-015', 1, @cid),
	('item-016', 0, @cid),
	('item-017', 0, null),
	('item-018', 1, null),
	('item-019', 1, @cid),
	('item-019', 1, null),
	('item-020', 0, null)
go
