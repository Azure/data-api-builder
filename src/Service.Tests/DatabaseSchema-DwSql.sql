-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

BEGIN TRANSACTION
DROP VIEW IF EXISTS books_view_all;
DROP VIEW IF EXISTS books_view_with_mapping;
DROP VIEW IF EXISTS books_publishers_view_composite;
DROP TABLE IF EXISTS book_author_link;
DROP TABLE IF EXISTS reviews;
DROP TABLE IF EXISTS authors;
DROP TABLE IF EXISTS book_website_placements;
DROP TABLE IF EXISTS website_users;
DROP TABLE IF EXISTS books;
DROP TABLE IF EXISTS [foo].[magazines];
DROP TABLE IF EXISTS stocks;
DROP TABLE IF EXISTS comics;
DROP TABLE IF EXISTS series;
DROP TABLE IF EXISTS sales;
DROP TABLE IF EXISTS GQLmappings;
DROP TABLE IF EXISTS publishers;
DROP SCHEMA IF EXISTS [foo];
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

INSERT INTO authors(id, name, birthdate) VALUES (123, 'Jelte', '2001-01-01'), (124, 'Aniruddh', '2002-02-02'), (125, 'Aniruddh', '2001-01-01'), (126, 'Aaron', '2001-01-01');

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
(14, 'Before Sunset', 1234);

INSERT INTO book_website_placements(id, book_id, price) VALUES (1, 1, 100), (2, 2, 50), (3, 3, 23), (4, 5, 33);

INSERT INTO reviews(id, book_id, content) VALUES (567, 1, 'Indeed a great book'), (568, 1, 'I loved it'), (569, 1, 'best book I read in years');

INSERT INTO sales(id, item_name, subtotal, tax) VALUES (1, 'Watch', 249.00, 20.59), (2, 'Montior', 120.50, 11.12);


INSERT INTO website_users(id, username) VALUES (1, 'George'), (2, NULL), (3, ''), (4, 'book_lover_95'), (5, 'null');

INSERT INTO series(id, [name]) VALUES (3001, 'Foundation'), (3002, 'Hyperion Cantos');

INSERT INTO comics(id, title, categoryName, series_id)
VALUES (1, 'Star Trek', 'SciFi', NULL), (2, 'Cinderella', 'Tales', 3001),(3,'Únknown','', 3002), (4, 'Alexander the Great', 'Historical', NULL),
(5, 'Snow White', 'AnotherTales', 3001);

INSERT INTO [foo].[magazines](id, title, issue_number) VALUES (1, 'Vogue', 1234), (11, 'Sports Illustrated', NULL), (3, 'Fitness', NULL);
INSERT INTO publishers(id, name) VALUES (1234, 'Big Company'), (2345, 'Small Town Publisher'), (2323, 'TBD Publishing One'), (2324, 'TBD Publishing Two Ltd'), (1940, 'Policy Publisher 01'), (1941, 'Policy Publisher 02'), (1156, 'The First Publisher');
INSERT INTO book_author_link(book_id, author_id) VALUES (1, 123), (2, 124), (3, 123), (3, 124), (4, 123), (4, 124), (5, 126);
INSERT INTO stocks(categoryid, pieceid, categoryName, piecesAvailable, piecesRequired) VALUES (1,1,'SciFi',0,0),(2,1,'Tales',0,0),(0,1,'',0,0),(100,99,'Historical',0,0);


EXEC('CREATE VIEW books_view_all AS SELECT * FROM dbo.books');
EXEC('CREATE VIEW books_view_with_mapping AS SELECT * FROM dbo.books');
EXEC('CREATE VIEW books_publishers_view_composite as SELECT
      publishers.name,books.id, books.title, publishers.id as pub_id
      FROM dbo.books,dbo.publishers
      where publishers.id = books.publisher_id');
