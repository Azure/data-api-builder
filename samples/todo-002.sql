drop table if exists s002.todos
drop table if exists s002.categories
drop sequence if exists s002.globalId
go

create sequence s002.globalId
as int start with 10000
go

create table s002.categories
(
	id int not null primary key default (next value for s002.globalId),
	category nvarchar(100) not null unique
)
go

create table s002.todos
(
	id int not null constraint pk__s002_todos primary key default (next value for s002.globalId),
	category_id int not null constraint fk__s002_todos2__s002_categories references s002.categories(id),
	title nvarchar(100) not null,
	[description] nvarchar(1000) null,
	completed bit not null default(0)
)
go

insert into s002.categories
	(id, category)
values
	(1, 'Family'),
	(2, 'Work'),
	(3, 'Hobby'),
	(4, 'Car'),
	(5, 'Bike')
go

insert into s002.todos
	(title, completed, category_id)
values
	('item-001', 0, 1),
	('item-002', 1, 1),
	('item-003', 1, 2),
	('item-004', 0, 3),
	('item-005', 1, 4),
	('item-006', 0, 3),
	('item-007', 0, 4),
	('item-008', 1, 4),
	('item-009', 1, 1),
	('item-010', 0, 2),
	('item-011', 1, 3),
	('item-012', 0, 4),
	('item-013', 1, 5),
	('item-015', 1, 1),
	('item-016', 0, 2),
	('item-017', 0, 4),
	('item-018', 1, 5),
	('item-019', 1, 3),
	('item-019', 1, 1),
	('item-020', 0, 2)
