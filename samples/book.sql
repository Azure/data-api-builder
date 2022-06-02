drop table if exists dbo.books;
drop table if exists dbo.categories;
drop sequence if exists dbo.globalId;
go

create sequence dbo.globalId
as int start with 10000;
go

create table dbo.categories
(
	id int not null primary key default(next value for dbo.globalId),
	category nvarchar(100) not null unique,
);
go

create table dbo.books
(
	id int not null primary key default(next value for dbo.globalId),
	title nvarchar(100) not null unique,
    [description] nvarchar(1000) not null,
    category_id int not null foreign key references dbo.categories(id)
);
go

insert into dbo.categories
    (id, category)
values
    (1, 'Science Fiction')
    ,(2, 'Non-Fiction')
;

insert into dbo.books
    (category_id, title, [description])
values
    (1, 'The Caves of Steel', 'The Caves of Steel is a science fiction novel by American writer Isaac Asimov. It is a detective story and illustrates an idea Asimov advocated, that science fiction can be applied to any literary genre, rather than just being a limited genre in itself.')
    ,(1, 'The Naked Sun', 'The Naked Sun is a science fiction novel by American writer Isaac Asimov, the second in his Robot series. Like its predecessor, The Caves of Steel, this is a whodunit story.')
    ,(1, 'The Robots of Dawn', 'The Robots of Dawn is a "whodunit" science fiction novel by American writer Isaac Asimov, first published in 1983. It is the third novel in Asimov''s Robot series.')
    ,(2, 'Built to Last: Successful Habits of Visionary Companies', 'Built to Last: Successful Habits of Visionary Companies is a book written by Jim Collins and Jerry I. Porras. It outlines the results of a six-year research project exploring what leads to enduringly great companies.')
;

