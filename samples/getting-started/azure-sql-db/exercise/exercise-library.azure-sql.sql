drop table if exists dbo.series;

create table dbo.series
(
    id int not null primary key default (next value for dbo.globalId),
    [serie] nvarchar(1000) not null
)
go

alter table dbo.books 
    add serie_id int null;
go

alter table dbo.books 
    add foreign key (serie_id) references dbo.series(id)
go

insert into dbo.series values
    (10000, 'Foundation'),
    (10001, 'Hyperion Cantos')
go

update dbo.books 
set serie_id = 10000
where id in (1000, 1001, 1002, 1003, 1004, 1005, 1006)
go

update dbo.books 
set serie_id = 10001
where id in (1013, 1014, 1015, 1016)
go

