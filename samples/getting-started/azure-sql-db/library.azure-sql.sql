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
    title nvarchar(1000) not null,
    [year] int null,
    [pages] int null
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
    (3, 'Robert', null, 'Silvenberg'),
    (4, 'Dan', null, 'Simmons')
go

insert into dbo.books values
    (1000, 'Prelude to Foundation', 1988, 403),
    (1001, 'Forward the Foundation', 1993, 417),
    (1002, 'Foundation', 1951, 255),
    (1003, 'Foundation and Empire', 1952, 247),
    (1004, 'Second Foundation', 1953, 210),
    (1005, 'Foundation''s Edge', 1982, 367),
    (1006, 'Foundation and Earth', 1986, 356),
    (1007, 'Nemesis', 1989, 386),
    (1008, 'Starship Troopers', null, null),
    (1009, 'Stranger in a Strange Land', null, null),
    (1010, 'Nightfall', null, null),
    (1011, 'Nightwings', null, null),
    (1012, 'Across a Billion Years', null, null),
    (1013, 'Hyperion', 1989, 482),
    (1014, 'The Fall of Hyperion', 1990, 517),
    (1015, 'Endymion', 1996, 441),
    (1016, 'The Rise of Endymion', 1997, 579)
go

insert into dbo.books_authors values
    (1, 1000),
    (1, 1001),
    (1, 1002),
    (1, 1003),
    (1, 1004),
    (1, 1005),
    (1, 1006),
    (1, 1007),
    (1, 1010),
    (2, 1008),
    (2, 1009),
    (2, 1011),
    (3, 1010),
    (3, 1012),
    (4, 1013),
    (4, 1014),
    (4, 1015),
    (4, 1016)
go

