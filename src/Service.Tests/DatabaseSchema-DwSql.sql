-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

BEGIN TRANSACTION
DROP VIEW IF EXISTS books_view_all;
DROP VIEW IF EXISTS books_view_with_mapping;
DROP VIEW IF EXISTS books_publishers_view_composite;
DROP VIEW IF EXISTS stocks_view_selected;
DROP TABLE IF EXISTS book_author_link;
DROP TABLE IF EXISTS reviews;
DROP TABLE IF EXISTS authors;
DROP TABLE IF EXISTS book_website_placements;
DROP TABLE IF EXISTS website_users;
DROP TABLE IF EXISTS books;
DROP TABLE IF EXISTS [foo].[magazines];
DROP TABLE IF EXISTS [bar].[magazines];
DROP TABLE IF EXISTS stocks_price;
DROP TABLE IF EXISTS stocks;
DROP TABLE IF EXISTS comics;
DROP TABLE IF EXISTS brokers;
DROP TABLE IF EXISTS type_table;
DROP TABLE IF EXISTS trees;
DROP TABLE IF EXISTS fungi;
DROP TABLE IF EXISTS empty_table;
DROP TABLE IF EXISTS notebooks;
DROP TABLE IF EXISTS journals;
DROP TABLE IF EXISTS aow;
DROP TABLE IF EXISTS series;
DROP TABLE IF EXISTS sales;
DROP TABLE IF EXISTS GQLmappings;
DROP TABLE IF EXISTS bookmarks;
DROP TABLE IF EXISTS mappedbookmarks;
DROP TABLE IF EXISTS publishers;
DROP TABLE IF EXISTS authors_history;
DROP TABLE IF EXISTS [DimAccount]
DROP PROCEDURE IF EXISTS get_books;
DROP PROCEDURE IF EXISTS get_book_by_id;
DROP PROCEDURE IF EXISTS get_publisher_by_id;
DROP PROCEDURE IF EXISTS count_books;
DROP PROCEDURE IF EXISTS get_authors_history_by_first_name;
DROP PROCEDURE IF EXISTS insert_book;
DROP PROCEDURE IF EXISTS delete_last_inserted_book;
DROP PROCEDURE IF EXISTS update_book_title;
DROP PROCEDURE IF EXISTS insert_and_display_all_books_for_given_publisher;
DROP SCHEMA IF EXISTS [foo];
DROP SCHEMA IF EXISTS [bar];
COMMIT;

CREATE TABLE books(
    id int NOT NULL,
    title varchar(2048) NOT NULL,
    publisher_id int NOT NULL
);

CREATE TABLE book_website_placements(
    id int NOT NULL,
    book_id int NOT NULL,
    price int NOT NULL
);

CREATE TABLE website_users(
    id int NOT NULL,
    username varchar(100) NULL
);

CREATE TABLE authors(
    id int,
    name varchar(2048) NOT NULL,
    birthdate varchar(2048) NOT NULL
);

CREATE TABLE reviews(
    book_id int,
    id int ,
    content varchar(2048) NOT NULL
);

CREATE TABLE book_author_link(
    book_id int NOT NULL,
    author_id int NOT NULL
);

EXEC('CREATE SCHEMA [foo]');

CREATE TABLE [foo].[magazines](
    id int NOT NULL,
    title varchar(2048) NOT NULL,
    issue_number int NULL
);

EXEC('CREATE SCHEMA [bar]');

CREATE TABLE [bar].[magazines](
    upc int NOT NULL,
    comic_name varchar(2048) NOT NULL,
    issue int NULL
);

CREATE TABLE comics(
    id int,
    title varchar(2048) NOT NULL,
    volume int ,
    categoryName varchar(100) NOT NULL,
    series_id int NULL
);

CREATE TABLE series (
    id int NOT NULL,
    [name] varchar(1000) NOT NULL
);

CREATE TABLE sales (
    id int NOT NULL,
    item_name varchar(2048) NOT NULL,
    subtotal decimal(18,2) NOT NULL,
    tax decimal(18,2) NOT NULL
);

CREATE TABLE GQLmappings (
    __column1 int,
    __column2 varchar(2048),
    column3 varchar(2048)
)

CREATE TABLE bookmarks
(
	id int,
	bkname varchar(1000) NOT NULL
)

CREATE TABLE mappedbookmarks
(
	id int ,
	bkname varchar(50) NOT NULL
)

CREATE TABLE publishers(
    id int NOT NULL,
    name varchar(2048) NOT NULL
);

CREATE TABLE stocks(
    categoryid int NOT NULL,
    pieceid int NOT NULL,
    categoryName varchar(100) NOT NULL,
    piecesAvailable int ,
    piecesRequired int NOT NULL
);


CREATE TABLE stocks_price(
    categoryid int NOT NULL,
    pieceid int NOT NULL,
    instant datetime NOT NULL,
    price int,
    is_wholesale_price bit
);

CREATE TABLE brokers(
    [ID Number] int NOT NULL,
    [First Name] varchar(2048) NOT NULL,
    [Last Name] varchar(2048) NOT NULL
);

CREATE TABLE empty_table (
    id int 
);

CREATE TABLE notebooks (
    id int ,
    notebookname varchar(2048),
    color varchar(2048),
    ownername varchar(2048)
);

CREATE TABLE journals (
    id int,
    journalname varchar(2048),
    color varchar(2048),
    ownername varchar(2048)
);

CREATE TABLE aow (
    NoteNum int,
    DetailAssessmentAndPlanning varchar(2048),
    WagingWar varchar(2048),
    StrategicAttack varchar(2048)
);

CREATE TABLE trees (
    treeId int,
    species varchar(2048),
    region varchar(2048),
    height varchar(2048)
);

CREATE TABLE fungi (
    speciesid int,
    region varchar(2048),
    habitat varchar(6)
);

CREATE TABLE type_table(
    id int ,
    short_types smallint,
    int_types int,
    long_types bigint,
    string_types varchar(2048),
    nvarchar_string_types varchar(2048),
    single_types real,
    float_types float(53),
    decimal_types decimal(18,3),
    boolean_types bit,
    date_types date,
    datetime_types datetime2(6),
    datetime2_types datetime2(6),
    time_types time(3),
    bytearray_types varbinary(2048),
    uuid_types uniqueidentifier
);

CREATE TABLE authors_history (
    id int NOT NULL,
    first_name varchar(100) NOT NULL,
    middle_name varchar(100),
    last_name varchar(100) NOT NULL,
    year_of_publish int,
    books_published int
);

CREATE TABLE [dbo].[DimAccount] (
    [AccountKey]                    [INT]           IDENTITY(1, 1) NOT NULL,
    [ParentAccountKey]              [INT]           NULL,
    CONSTRAINT [PK_DimAccount]
        PRIMARY KEY CLUSTERED ([AccountKey] ASC)
);

ALTER TABLE [dbo].[DimAccount] WITH CHECK
ADD CONSTRAINT [FK_DimAccount_DimAccount]
    FOREIGN KEY ([ParentAccountKey])
    REFERENCES [dbo].[DimAccount] ([AccountKey]);

ALTER TABLE [dbo].[DimAccount] CHECK CONSTRAINT [FK_DimAccount_DimAccount];

SET IDENTITY_INSERT DimAccount ON
INSERT INTO DimAccount(AccountKey, ParentAccountKey)
VALUES (1, null),
(2, 1),
(3, 2),
(4, 2);
SET IDENTITY_INSERT DimAccount OFF

EXEC('CREATE PROCEDURE get_publisher_by_id @id int AS
      SELECT * FROM dbo.publishers
      WHERE id = @id');
EXEC('CREATE PROCEDURE get_books AS
      SELECT * FROM dbo.books');
EXEC('CREATE PROCEDURE get_book_by_id @id int AS
      SELECT * FROM dbo.books
      WHERE id = @id');
EXEC('CREATE PROCEDURE count_books AS
	  SELECT COUNT(*) AS total_books FROM dbo.books');
EXEC('CREATE PROCEDURE get_authors_history_by_first_name @firstName varchar(100) AS
      BEGIN
        SELECT
          concat(first_name, '' '', (middle_name + '' ''), last_name) as author_name,
          min(year_of_publish) as first_publish_year,
          sum(books_published) as total_books_published
        FROM
          authors_history
        WHERE
          first_name=@firstName
        GROUP BY
          concat(first_name, '' '', (middle_name + '' ''), last_name)
      END');
EXEC('CREATE PROCEDURE insert_book @book_id int, @title varchar(max), @publisher_id int AS
      INSERT INTO dbo.books(id, title, publisher_id) VALUES (@book_id, @title, @publisher_id)');
EXEC('CREATE PROCEDURE delete_last_inserted_book AS
      BEGIN
        DELETE FROM dbo.books
        WHERE
        id = (select max(id) from dbo.books)
      END');
EXEC('CREATE PROCEDURE update_book_title @id int, @title varchar(max) AS
      BEGIN
        UPDATE dbo.books SET title = @title WHERE id = @id
        SELECT * from dbo.books WHERE id = @id
      END');
EXEC('CREATE PROCEDURE insert_and_display_all_books_for_given_publisher @book_id int,@title varchar(max), @publisher_name varchar(max) AS
      BEGIN
        DECLARE @publisher_id AS INT;
        SET @publisher_id = (SELECT id FROM dbo.publishers WHERE name = @publisher_name);
        INSERT INTO dbo.books(id, title, publisher_id)
        VALUES(@book_id, @title, @publisher_id);

        SELECT * FROM dbo.books WHERE publisher_id = @publisher_id;
      END');
INSERT INTO authors(id, name, birthdate) VALUES (123, 'Jelte', '2001-01-01'), (124, 'Aniruddh', '2002-02-02'), (125, 'Aniruddh', '2001-01-01'), (126, 'Aaron', '2001-01-01');

INSERT INTO authors_history(id, first_name, middle_name, last_name, year_of_publish, books_published)
VALUES
(1, 'Isaac', null, 'Asimov', 1993, 6),
(2, 'Robert', 'A.', 'Heinlein', 1886, null),
(3, 'Robert', null, 'Silvenberg', null, null),
(4, 'Dan', null, 'Simmons', 1759, 3),
(5, 'Isaac', null, 'Asimov', 2000, null),
(6, 'Robert', 'A.', 'Heinlein', 1899, 2),
(7, 'Isaac', null, 'Silvenberg', 1664, null),
(8, 'Dan', null, 'Simmons', 1799, 3),
(9, 'Aaron', null, 'Mitchells', 2001, 1),
(10, 'Aaron', 'F.', 'Burtle', null, null)

INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (1, 'Incompatible GraphQL Name', 'Compatible GraphQL Name');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (3, 'Old Value', 'Record to be Updated');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (4, 'Lost Record', 'Record to be Deleted');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (5, 'Filtered Record', 'Record to be Filtered on Find');

INSERT INTO books(id, title, publisher_id)
VALUES (1, 'Awesome book', 1234),
(2, 'Also Awesome book', 1234),
(3, 'Great wall of china explained', 2345),
(4, 'US history in a nutshell', 2345),
(5, 'Chernobyl Diaries', 2323),
(6, 'The Palace Door', 2324),
(7, 'The Groovy Bar', 2324),
(8, 'Time to Eat', 2324),
(9, 'Policy-Test-01', 1940),
(10, 'Policy-Test-02', 1940),
(11, 'Policy-Test-04', 1941),
(12, 'Time to Eat 2', 1941),
(13, 'Before Sunrise', 1234),
(14, 'Before Sunset', 1234),
(15, 'SQL_CONN', 1234),
(16, 'SOME%CONN', 1234),
(17, 'CONN%_CONN', 1234),
(18, '[Special Book]', 1234),
(19, 'ME\YOU', 1234),
(20, 'C:\\LIFE', 1234);

INSERT INTO book_website_placements(id, book_id, price) VALUES (1, 1, 100), (2, 2, 50), (3, 3, 23), (4, 5, 33);

INSERT INTO reviews(id, book_id, content) VALUES (567, 1, 'Indeed a great book'), (568, 1, 'I loved it'), (569, 1, 'best book I read in years');

INSERT INTO sales(id, item_name, subtotal, tax) VALUES (1, 'Watch', 249.00, 20.59), (2, 'Montior', 120.50, 11.12);


INSERT INTO website_users(id, username) VALUES (1, 'George'), (2, NULL), (3, ''), (4, 'book_lover_95'), (5, 'null');

INSERT INTO series(id, [name]) VALUES (3001, 'Foundation'), (3002, 'Hyperion Cantos');

INSERT INTO comics(id, title, categoryName, series_id)
VALUES (1, 'Star Trek', 'SciFi', NULL), (2, 'Cinderella', 'Tales', 3001),(3,'Únknown','', 3002), (4, 'Alexander the Great', 'Historical', NULL),
(5, 'Snow White', 'AnotherTales', 3001);

INSERT INTO [foo].[magazines](id, title, issue_number) VALUES (1, 'Vogue', 1234), (11, 'Sports Illustrated', NULL), (3, 'Fitness', NULL);
INSERT INTO [bar].[magazines](upc, comic_name, issue) VALUES (0, 'NotVogue', 0);
INSERT INTO brokers([ID Number], [First Name], [Last Name]) VALUES (1, 'Michael', 'Burry'), (2, 'Jordan', 'Belfort');
INSERT INTO publishers(id, name) VALUES (1234, 'Big Company'), (2345, 'Small Town Publisher'), (2323, 'TBD Publishing One'), (2324, 'TBD Publishing Two Ltd'), (1940, 'Policy Publisher 01'), (1941, 'Policy Publisher 02'), (1156, 'The First Publisher');
INSERT INTO book_author_link(book_id, author_id) VALUES (1, 123), (2, 124), (3, 123), (3, 124), (4, 123), (4, 124), (5, 126);
INSERT INTO stocks(categoryid, pieceid, categoryName, piecesAvailable, piecesRequired) VALUES (1,1,'SciFi',0,0),(2,1,'Tales',0,0),(0,1,'',0,0),(100,99,'Historical',0,0);
INSERT INTO stocks_price (categoryid, pieceid, instant, price, is_wholesale_price) VALUES (2, 1, '2023-08-21 15:11:04', 100, 1);
INSERT INTO notebooks(id, notebookname, color, ownername) VALUES (1, 'Notebook1', 'red', 'Sean'), (2, 'Notebook2', 'green', 'Ani'), (3, 'Notebook3', 'blue', 'Jarupat'), (4, 'Notebook4', 'yellow', 'Aaron');
INSERT INTO journals(id, journalname, color, ownername)
VALUES
    (1, 'Journal1', 'red', 'Sean'),
    (2, 'Journal2', 'green', 'Ani'),
    (3, 'Journal3', 'blue', 'Jarupat'),
    (4, 'Journal4', 'yellow', 'Aaron'),
    (5, 'Journal5', null, 'Abhishek'),
    (6, 'Journal6', 'green', null),
    (7, 'Journal7', null, null);
INSERT INTO aow(NoteNum, DetailAssessmentAndPlanning, WagingWar, StrategicAttack) VALUES (1, 'chapter one notes: ', 'chapter two notes: ', 'chapter three notes: ');
INSERT INTO trees(treeId, species, region, height) VALUES (1, 'Tsuga terophylla', 'Pacific Northwest', '30m'), (2, 'Pseudotsuga menziesii', 'Pacific Northwest', '40m');
INSERT INTO trees(treeId, species, region, height) VALUES (4, 'test', 'Pacific Northwest', '0m');
INSERT INTO fungi(speciesid, region, habitat) VALUES (1, 'northeast', 'forest'), (2, 'southwest', 'sand');
INSERT INTO fungi(speciesid, region, habitat) VALUES (3, 'northeast', 'test');
INSERT INTO type_table(id, short_types, int_types, long_types,
string_types, nvarchar_string_types,
single_types, float_types, decimal_types,
boolean_types,
date_types, datetime_types, datetime2_types, time_types,
bytearray_types)
VALUES
    (1, 1, 1, 1, '', '', 0.33, 0.33, 0.333333, 1,
    '1999-01-08', '1999-01-08 10:23:54', '1999-01-08 10:23:54.9999999', '10:23:54.9999999',
    0xABCDEF0123),
    (2, -1, -1, -1, 'lksa;jdflasdf;alsdflksdfkldj', 'lksa;jdflasdf;alsdflksdfkldj', -9.2, -9.2, -9.292929, 0,
    '1999-01-08', '1999-01-08 10:23:00', '1999-01-08 10:23:00.9999999', '10:23:00.9999999',
    0x98AB7511AABB1234),
    (3, -32768, -2147483648, -9223372036854775808, 'null', 'null', -3.4E38, -1.7E308, 2.929292E-19, 1,
    '0001-01-01', '1753-01-01 00:00:00.000', '0001-01-01 00:00:00.0000000', '00:00:00.0000000',
    0x00000000),
    (4, 32767, 2147483647, 9223372036854775807, 'null', 'null', 3.4E38, 1.7E308, 2.929292E-14, 1,
    '9999-12-31', '9999-12-31 23:59:59', '9999-12-31 23:59:59.9999999', '23:59:59.9999999',
    0xFFFFFFFF),
    (5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);

DECLARE @UpperBound INT = 10000;

INSERT INTO bookmarks ([id], [bkname])
SELECT TOP 10000
    ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Counter,
    'Test Item #' + FORMAT(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), '00000') AS bkname
FROM
    sys.all_columns AS t1
CROSS JOIN
    sys.all_columns AS t2
ORDER BY
    Counter

INSERT INTO mappedbookmarks ([id], [bkname])
SELECT TOP 10000
    ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Counter,
    'Test Item #' + FORMAT(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), '00000') AS bkname
FROM
    sys.all_columns AS t1
CROSS JOIN
    sys.all_columns AS t2
ORDER BY
    Counter

EXEC('CREATE VIEW books_view_all AS SELECT * FROM dbo.books');
EXEC('CREATE VIEW books_view_with_mapping AS SELECT * FROM dbo.books');
EXEC('CREATE VIEW books_publishers_view_composite as SELECT
      publishers.name,books.id, books.title, publishers.id as pub_id
      FROM dbo.books,dbo.publishers
      where publishers.id = books.publisher_id');
EXEC('CREATE VIEW stocks_view_selected AS SELECT
      categoryid,pieceid,categoryName,piecesAvailable
      FROM dbo.stocks');
