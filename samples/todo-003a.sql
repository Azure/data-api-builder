if schema_id('s003a') is null begin
	exec('create schema s003a authorization dbo')
end

drop table if exists s003a.todos_tags
drop table if exists s003a.todos
drop table if exists s003a.tags
drop table if exists s003a.categories
drop sequence if exists s003a.globalId
go

create sequence s003a.globalId
as int start with 10000
go

create table s003a.categories
(
	id varchar(3) collate Latin1_General_BIN2 not null primary key,
	category nvarchar(100) not null unique
)
go

create table s003a.todos
(
	id int not null constraint pk__s003a_todos primary key default (next value for s003a.globalId),
	category_id varchar(3) collate Latin1_General_BIN2 not null constraint fk__s003a_todos__s003a_categories references s003a.categories(id),
	title nvarchar(100) not null,
	[description] nvarchar(1000) null,
	completed bit not null default(0)
)
go

create table s003a.tags
(
	id int not null primary key default (next value for s003a.globalId),
	tag nvarchar(100) not null unique
)
go

create table s003a.todos_tags
(
	todo_id int not null constraint fk__s003a_todos_tags__s003a_todos references s003a.todos(id),
	tag_id int not null constraint fk__s003a_todos_tags__s003a_tags references s003a.tags(id),
	primary key (todo_id, tag_id)
)
go


insert into s003a.categories
	(id, category)
values
	('f', 'Family'),
	('w', 'Work'),
	('h', 'Hobby'),
	('c', 'Car'),
	('b', 'Bike')
go

insert into s003a.todos
	(title, completed, category_id)
values
	('item-001', 0, 'f'),
	('item-002', 1, 'f'),
	('item-003', 1, 'w'),
	('item-004', 0, 'h'),
	('item-005', 1, 'c'),
	('item-006', 0, 'w'),
	('item-007', 0, 'c'),
	('item-008', 1, 'c'),
	('item-009', 1, 'f'),
	('item-010', 0, 'w'),
	('item-011', 1, 'b'),
	('item-012', 0, 'b'),
	('item-013', 1, 'h'),
	('item-015', 1, 'c'),
	('item-016', 0, 'f'),
	('item-017', 0, 'w'),
	('item-018', 1, 'c'),
	('item-019', 1, 'h'),
	('item-019', 1, 'w'),
	('item-020', 0, 'f')
go

insert into s003a.tags
	(id, tag)
values
	(100, 'red'),
	(101, 'blue'),
	(102, 'gree'),
	(103, 'pink'),
	(104, 'jellow')
go

select * from s003a.todos

insert into s003a.todos_tags
	(todo_id, tag_id)
values
	(10000, 100),
	(10001, 100),
	(10001, 101),
	(10002, 101),
	(10003, 104),
	(10004, 103),
	(10004, 102),
	(10005, 101),
	(10005, 102),
	(10005, 103),
	(10005, 104),
	(10006, 103),
	(10008, 102),
	(10009, 102),
	(10010, 100),
	(10011, 101),
	(10014, 101),
	(10017, 102),
	(10017, 103),
	(10017, 101)
go
