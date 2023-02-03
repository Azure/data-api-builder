BEGIN TRANSACTION
DROP SECURITY POLICY IF EXISTS revenuesSecPolicy;
DROP FUNCTION IF EXISTS revenuesPredicate;
DROP VIEW IF EXISTS books_view_all;
DROP VIEW IF EXISTS books_view_with_mapping;
DROP VIEW IF EXISTS stocks_view_selected;
DROP VIEW IF EXISTS books_publishers_view_composite;
DROP VIEW IF EXISTS books_publishers_view_composite_insertable;
DROP PROCEDURE IF EXISTS get_books;
DROP PROCEDURE IF EXISTS get_book_by_id;
DROP PROCEDURE IF EXISTS get_publisher_by_id;
DROP PROCEDURE IF EXISTS insert_book;
DROP PROCEDURE IF EXISTS count_books;
DROP PROCEDURE IF EXISTS delete_last_inserted_book;
DROP PROCEDURE IF EXISTS update_book_title;
DROP PROCEDURE IF EXISTS get_authors_history_by_first_name;
DROP PROCEDURE IF EXISTS insert_and_display_all_books_for_given_publisher;
DROP TABLE IF EXISTS book_author_link;
DROP TABLE IF EXISTS reviews;
DROP TABLE IF EXISTS authors;
DROP TABLE IF EXISTS book_website_placements;
DROP TABLE IF EXISTS website_users;
DROP TABLE IF EXISTS books;
DROP TABLE IF EXISTS publishers;
DROP TABLE IF EXISTS [foo].[magazines];
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
DROP TABLE IF EXISTS authors_history;
DROP TABLE IF EXISTS revenues;
DROP TABLE IF EXISTS graphql_incompatible;
DROP TABLE IF EXISTS GQLmappings;
DROP SCHEMA IF EXISTS [foo];
COMMIT;

--Autogenerated id seed are set at 5001 for consistency with Postgres
--This allows for tests using the same id values for both languages
--Starting with id > 5000 is chosen arbitrarily so that the incremented id-s won't conflict with the manually inserted ids in this script
CREATE TABLE publishers(
    id int IDENTITY(5001, 1) PRIMARY KEY,
    name varchar(max) NOT NULL
);

CREATE TABLE books(
    id int IDENTITY(5001, 1) PRIMARY KEY,
    title varchar(max) NOT NULL,
    publisher_id int NOT NULL
);

CREATE TABLE book_website_placements(
    id int IDENTITY(5001, 1) PRIMARY KEY,
    book_id int UNIQUE NOT NULL,
    price int NOT NULL
);

CREATE TABLE website_users(
    id int PRIMARY KEY,
    username text NULL
);

CREATE TABLE authors(
    id int IDENTITY(5001, 1) PRIMARY KEY,
    name varchar(max) NOT NULL,
    birthdate varchar(max) NOT NULL
);

CREATE TABLE reviews(
    book_id int,
    id int IDENTITY(5001, 1),
    content varchar(max) DEFAULT('Its a classic') NOT NULL,
    PRIMARY KEY(book_id, id)
);

CREATE TABLE book_author_link(
    book_id int NOT NULL,
    author_id int NOT NULL,
    PRIMARY KEY(book_id, author_id)
);

EXEC('CREATE SCHEMA [foo]');

CREATE TABLE [foo].[magazines](
    id int PRIMARY KEY,
    title varchar(max) NOT NULL,
    issue_number int NULL
);

CREATE TABLE comics(
    id int PRIMARY KEY,
    title varchar(max) NOT NULL,
    volume int IDENTITY(5001,1),
    categoryName varchar(100) NOT NULL UNIQUE,
    series_id int NULL
);

CREATE TABLE stocks(
    categoryid int NOT NULL,
    pieceid int NOT NULL,
    categoryName varchar(100) NOT NULL,
    piecesAvailable int DEFAULT 0,
    piecesRequired int DEFAULT 0 NOT NULL,
    PRIMARY KEY(categoryid,pieceid)
);

CREATE TABLE stocks_price(
    categoryid int NOT NULL,
    pieceid int NOT NULL,
    instant datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    price float,
    is_wholesale_price bit,
    PRIMARY KEY(categoryid, pieceid, instant)
);

CREATE TABLE brokers(
    [ID Number] int PRIMARY KEY,
    [First Name] varchar(max) NOT NULL,
    [Last Name] varchar(max) NOT NULL
);

CREATE TABLE type_table(
    id int IDENTITY(5001, 1) PRIMARY KEY,
    byte_types tinyint,
    short_types smallint,
    int_types int,
    long_types bigint,
    string_types varchar(max),
    single_types real,
    float_types float,
    decimal_types decimal(38, 19),
    boolean_types bit,
    datetime_types datetime,
    bytearray_types varbinary(max),
    guid_types uniqueidentifier DEFAULT newid()
);

CREATE TABLE trees (
    treeId int PRIMARY KEY,
    species varchar(max),
    region varchar(max),
    height varchar(max)
);

CREATE TABLE fungi (
    speciesid int PRIMARY KEY,
    region varchar(max)
);

CREATE TABLE empty_table (
    id int PRIMARY KEY
);

CREATE TABLE notebooks (
    id int PRIMARY KEY,
    notebookname varchar(max),
    color varchar(max),
    ownername varchar(max)
);

CREATE TABLE journals (
    id int PRIMARY KEY,
    journalname varchar(max),
    color varchar(max),
    ownername varchar(max)
);

CREATE TABLE aow (
    NoteNum int PRIMARY KEY,
    DetailAssessmentAndPlanning varchar(max),
    WagingWar varchar(max),
    StrategicAttack varchar(max)
);

CREATE TABLE series (
    id int NOT NULL IDENTITY(5001, 1) PRIMARY KEY,
    [name] nvarchar(1000) NOT NULL
);

CREATE TABLE sales (
    id int NOT NULL IDENTITY(5001, 1) PRIMARY KEY,
    item_name varchar(max) NOT NULL,
    subtotal decimal(18,2) NOT NULL,
    tax decimal(18,2) NOT NULL
);

CREATE TABLE authors_history (
    id int NOT NULL IDENTITY(5001,1) PRIMARY KEY,
    first_name varchar(100) NOT NULL,
    middle_name varchar(100),
    last_name varchar(100) NOT NULL,
    year_of_publish int,
    books_published int
);

CREATE TABLE revenues(
    id int PRIMARY KEY,
    category varchar(max) NOT NULL,
    revenue int,
    accessible_role varchar(max) NOT NULL
);

CREATE TABLE graphql_incompatible (
    __typeName int PRIMARY KEY,
    conformingName varchar(12)
);

CREATE TABLE GQLmappings (
    __column1 int PRIMARY KEY,
    __column2 varchar(max),
    column3 varchar(max)
)

ALTER TABLE books
ADD CONSTRAINT book_publisher_fk
FOREIGN KEY (publisher_id)
REFERENCES publishers (id)
ON DELETE CASCADE;

ALTER TABLE book_website_placements
ADD CONSTRAINT book_website_placement_book_fk
FOREIGN KEY (book_id)
REFERENCES books (id)
ON DELETE CASCADE;

ALTER TABLE reviews
ADD CONSTRAINT review_book_fk
FOREIGN KEY (book_id)
REFERENCES books (id)
ON DELETE CASCADE;

ALTER TABLE book_author_link
ADD CONSTRAINT book_author_link_book_fk
FOREIGN KEY (book_id)
REFERENCES books (id)
ON DELETE CASCADE;

ALTER TABLE book_author_link
ADD CONSTRAINT book_author_link_author_fk
FOREIGN KEY (author_id)
REFERENCES authors (id)
ON DELETE CASCADE;

ALTER TABLE stocks
ADD CONSTRAINT stocks_comics_fk
FOREIGN KEY (categoryName)
REFERENCES comics (categoryName)
ON DELETE CASCADE;

ALTER TABLE stocks_price
ADD CONSTRAINT stocks_price_stocks_fk
FOREIGN KEY (categoryid, pieceid)
REFERENCES stocks (categoryid, pieceid)
ON DELETE CASCADE;

ALTER TABLE comics
ADD CONSTRAINT comics_series_fk
FOREIGN KEY (series_id)
REFERENCES series(id)
ON DELETE CASCADE;

ALTER TABLE sales
ADD total AS (subtotal + tax) PERSISTED;

SET IDENTITY_INSERT publishers ON
INSERT INTO publishers(id, name) VALUES (1234, 'Big Company'), (2345, 'Small Town Publisher'), (2323, 'TBD Publishing One'), (2324, 'TBD Publishing Two Ltd'), (1940, 'Policy Publisher 01'), (1941, 'Policy Publisher 02'), (1156, 'The First Publisher');
SET IDENTITY_INSERT publishers OFF

SET IDENTITY_INSERT authors ON
INSERT INTO authors(id, name, birthdate) VALUES (123, 'Jelte', '2001-01-01'), (124, 'Aniruddh', '2002-02-02'), (125, 'Aniruddh', '2001-01-01'), (126, 'Aaron', '2001-01-01');
SET IDENTITY_INSERT authors OFF

INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (1, 'Incompatible GraphQL Name', 'Compatible GraphQL Name');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (3, 'Old Value', 'Record to be Updated');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (4, 'Lost Record', 'Record to be Deleted');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (5, 'Filtered Record', 'Record to be Filtered on Find');

SET IDENTITY_INSERT books ON
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
SET IDENTITY_INSERT books OFF

SET IDENTITY_INSERT book_website_placements ON
INSERT INTO book_website_placements(id, book_id, price) VALUES (1, 1, 100), (2, 2, 50), (3, 3, 23), (4, 5, 33);
SET IDENTITY_INSERT book_website_placements OFF

INSERT INTO book_author_link(book_id, author_id) VALUES (1, 123), (2, 124), (3, 123), (3, 124), (4, 123), (4, 124), (5, 126);

SET IDENTITY_INSERT reviews ON
INSERT INTO reviews(id, book_id, content) VALUES (567, 1, 'Indeed a great book'), (568, 1, 'I loved it'), (569, 1, 'best book I read in years');
SET IDENTITY_INSERT reviews OFF

SET IDENTITY_INSERT type_table ON
INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, single_types, float_types, decimal_types, boolean_types, datetime_types, bytearray_types) VALUES
    (1, 1, 1, 1, 1, '', 0.33, 0.33, 0.333333, 1, '1999-01-08 10:23:54', 0xABCDEF0123),
    (2, 0, -1, -1, -1, 'lksa;jdflasdf;alsdflksdfkldj', -9.2, -9.2, -9.292929, 0, '1999-01-08 10:23:00', 0x98AB7511AABB1234),
    (3, 0, -32768, -2147483648, -9223372036854775808, 'null', -3.4E38, -1.7E308, 2.929292E-19, 1, '1753-01-01 00:00:00.000', 0x00000000),
    (4, 255, 32767, 2147483647, 9223372036854775807, 'null', 3.4E38, 1.7E308, 2.929292E-14, 1, '9999-12-31 23:59:59', 0xFFFFFFFF),
    (5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);
SET IDENTITY_INSERT type_table OFF

SET IDENTITY_INSERT sales ON
INSERT INTO sales(id, item_name, subtotal, tax) VALUES (1, 'Watch', 249.00, 20.59), (2, 'Montior', 120.50, 11.12);
SET IDENTITY_INSERT sales OFF

INSERT INTO notebooks(id, notebookname, color, ownername) VALUES (1, 'Notebook1', 'red', 'Sean'), (2, 'Notebook2', 'green', 'Ani'), (3, 'Notebook3', 'blue', 'Jarupat'), (4, 'Notebook4', 'yellow', 'Aaron');
INSERT INTO journals(id, journalname, color, ownername) VALUES (1, 'Journal1', 'red', 'Sean'), (2, 'Journal2', 'green', 'Ani'), (3, 'Journal3', 'blue', 'Jarupat'), (4, 'Journal4', 'yellow', 'Aaron');

INSERT INTO website_users(id, username) VALUES (1, 'George'), (2, NULL), (3, ''), (4, 'book_lover_95'), (5, 'null');
INSERT INTO [foo].[magazines](id, title, issue_number) VALUES (1, 'Vogue', 1234), (11, 'Sports Illustrated', NULL), (3, 'Fitness', NULL);
INSERT INTO brokers([ID Number], [First Name], [Last Name]) VALUES (1, 'Michael', 'Burry'), (2, 'Jordan', 'Belfort');

SET IDENTITY_INSERT series ON
INSERT INTO series(id, [name]) VALUES (3001, 'Foundation'), (3002, 'Hyperion Cantos');
SET IDENTITY_INSERT series OFF

INSERT INTO comics(id, title, categoryName, series_id)
VALUES (1, 'Star Trek', 'SciFi', NULL), (2, 'Cinderella', 'FairyTales', 3001),(3,'Únknown','', 3002), (4, 'Alexander the Great', 'Historical', NULL),
(5, 'Snow White', 'Fairy Tales', 3001);
INSERT INTO stocks(categoryid, pieceid, categoryName) VALUES (1, 1, 'SciFi'), (2, 1, 'FairyTales'),(0,1,''),(100, 99, 'Historical');
INSERT INTO stocks_price(categoryid, pieceid, price, is_wholesale_price) VALUES (2, 1, 100.57, 1), (1, 1, 42.75, 0), (100, 99, NULL, NULL);
INSERT INTO trees(treeId, species, region, height) VALUES (1, 'Tsuga terophylla', 'Pacific Northwest', '30m'), (2, 'Pseudotsuga menziesii', 'Pacific Northwest', '40m');
INSERT INTO aow(NoteNum, DetailAssessmentAndPlanning, WagingWar, StrategicAttack) VALUES (1, 'chapter one notes: ', 'chapter two notes: ', 'chapter three notes: ');
INSERT INTO fungi(speciesid, region) VALUES (1, 'northeast'), (2, 'southwest');

SET IDENTITY_INSERT authors_history ON
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
SET IDENTITY_INSERT authors_history OFF

INSERT INTO revenues(id, category, revenue, accessible_role) VALUES (1, 'Book', 5000, 'Anonymous'), (2, 'Comics', 10000, 'Anonymous'),
(3, 'Journals', 20000, 'Authenticated'), (4, 'Series', 40000, 'Authenticated');

EXEC('CREATE VIEW books_view_all AS SELECT * FROM dbo.books');
EXEC('CREATE VIEW books_view_with_mapping AS SELECT * FROM dbo.books');
EXEC('CREATE VIEW stocks_view_selected AS SELECT
      categoryid,pieceid,categoryName,piecesAvailable
      FROM dbo.stocks');
EXEC('CREATE VIEW books_publishers_view_composite as SELECT
      publishers.name,books.id, books.title, publishers.id as pub_id
      FROM dbo.books,dbo.publishers
      where publishers.id = books.publisher_id');
EXEC('CREATE VIEW books_publishers_view_composite_insertable as SELECT
      books.id, books.title, publishers.name, books.publisher_id
      FROM dbo.books,dbo.publishers
      where publishers.id = books.publisher_id');
EXEC('CREATE PROCEDURE get_book_by_id @id int AS
      SELECT * FROM dbo.books
      WHERE id = @id');
EXEC('CREATE PROCEDURE get_publisher_by_id @id int AS
      SELECT * FROM dbo.publishers
      WHERE id = @id');
EXEC('CREATE PROCEDURE get_books AS
      SELECT * FROM dbo.books');
EXEC('CREATE PROCEDURE insert_book @title varchar(max), @publisher_id int AS
      INSERT INTO dbo.books(title, publisher_id) VALUES (@title, @publisher_id)');
EXEC('CREATE PROCEDURE count_books AS
	  SELECT COUNT(*) AS total_books FROM dbo.books');
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
EXEC('CREATE PROCEDURE insert_and_display_all_books_for_given_publisher @title varchar(max), @publisher_name varchar(max) AS
      BEGIN
        DECLARE @publisher_id AS INT;
        SET @publisher_id = (SELECT id FROM dbo.publishers WHERE name = @publisher_name);
        INSERT INTO dbo.books(title, publisher_id)
        VALUES(@title, @publisher_id);

        SELECT * FROM dbo.books WHERE publisher_id = @publisher_id;
      END');

-- Create a function to be used as a filter predicate by the security policy to restrict access to rows in the table for SELECT,UPDATE,DELETE operations.
-- Users with roles(claim value) = @accessible_role(column value) or,
-- Users with roles(claim value) = null and @accessible_role(column value) = 'Anonymous',
-- will be able to access a particular row.
EXEC('CREATE FUNCTION dbo.revenuesPredicate(@accessible_role varchar(20))
    RETURNS TABLE
    WITH SCHEMABINDING
    AS RETURN SELECT 1 AS fn_securitypredicate_result
    WHERE @accessible_role = CAST(SESSION_CONTEXT(N''roles'') AS varchar(20)) or (SESSION_CONTEXT(N''roles'') is null and @accessible_role=''Anonymous'')');

-- Adding a security policy which would restrict access to the rows in revenues table for
-- SELECT,UPDATE,DELETE operations using the filter predicate dbo.revenuesPredicate.
EXEC('CREATE SECURITY POLICY dbo.revenuesSecPolicy ADD FILTER PREDICATE dbo.revenuesPredicate(accessible_role) ON dbo.revenues;');
