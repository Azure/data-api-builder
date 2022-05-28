drop table if exists dbo.books
drop sequence if exists dbo.globalId
go

create sequence dbo.globalId
as int start with 10000
go

create table dbo.books
(
	id int not null primary key default(next value for dbo.globalId),
	title nvarchar(100) not null unique,
    [description] nvarchar(1000) not null
)
go
