drop table if exists dbo.books_authors;
drop table if exists dbo.books;
drop table if exists dbo.authors;
drop sequence if exists dbo.globalId;
go

create sequence dbo.globalId
as int 
start with 1000000
increment by 1
go

create table dbo.books
(
    id int not null primary key default (next value for dbo.globalId),
    title nvarchar(1000) not null
)
go

create table dbo.authors
(
    id int not null primary key default (next value for dbo.globalId),
    first_name nvarchar(100) not null,
    middle_name  nvarchar(100) null,
    last_name nvarchar(100) not null
)
go

create table dbo.books_authors
(
    author_id int not null foreign key references dbo.authors(id),
    book_id int not null foreign key references dbo.books(id),
    primary key (        
        author_id,
        book_id
    )
)
go

create nonclustered index ixncu1 on dbo.books_authors(book_id, author_id)
go

insert into dbo.authors values  
    (1, 'Isaac', null, 'Asimov'),
    (2, 'Robert', 'A.', 'Heinlein'),
    (3, 'Robert', null, 'Silvenberg')
go

insert into dbo.books values  
    (1000, 'Foundation'),
    (1001, 'Foundation and Empire'),
    (1002, 'Second Foundation'),
    (1003, 'Foundation''s Edge'),
    (1004, 'Foundation and Earth'),
    (1005, 'Nemesis'),
    (1006, 'Starship Troopers'),
    (1007, 'Stranger in a Strange Land'),
    (1008, 'Nightfall'),
    (1009, 'Nightwings'),
    (1010, 'Across a Billion Years')
go

insert into dbo.books_authors values
    (1, 1000),
    (1, 1001),
    (1, 1002),
    (1, 1003),
    (1, 1004),
    (1, 1005),
    (2, 1006),
    (2, 1007),
    (1, 1008),
    (3, 1008),
    (3, 1009),
    (3, 1010)