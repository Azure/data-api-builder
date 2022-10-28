DROP VIEW IF EXISTS books_view_all;
DROP VIEW IF EXISTS stocks_view_selected;
DROP VIEW IF EXISTS books_publishers_view_composite;
DROP TABLE IF EXISTS book_author_link;
DROP TABLE IF EXISTS reviews;
DROP TABLE IF EXISTS authors;
DROP TABLE IF EXISTS book_website_placements;
DROP TABLE IF EXISTS website_users;
DROP TABLE IF EXISTS books;
DROP TABLE IF EXISTS publishers;
DROP TABLE IF EXISTS magazines;
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


CREATE TABLE publishers(
    id int AUTO_INCREMENT PRIMARY KEY,
    name text NOT NULL
);

CREATE TABLE books(
    id int AUTO_INCREMENT PRIMARY KEY,
    title text NOT NULL,
    publisher_id int NOT NULL
);

CREATE TABLE book_website_placements(
    id int AUTO_INCREMENT PRIMARY KEY,
    book_id int UNIQUE NOT NULL,
    price int NOT NULL
);

CREATE TABLE website_users(
    id int PRIMARY KEY,
    username text NULL
);

CREATE TABLE authors(
    id int AUTO_INCREMENT PRIMARY KEY,
    name text NOT NULL,
    birthdate text NOT NULL
);

CREATE TABLE reviews(
    book_id int NOT NULL,
    id int AUTO_INCREMENT,
    content text DEFAULT ('Its a classic') NOT NULL,
    PRIMARY KEY(book_id, id),
    INDEX (id)
);

CREATE TABLE book_author_link(
    book_id int NOT NULL,
    author_id int NOT NULL,
    PRIMARY KEY(book_id, author_id)
);

CREATE TABLE magazines(
    id int PRIMARY KEY,
    title text NOT NULL,
    issue_number int NULL
);

CREATE TABLE comics(
    id int PRIMARY KEY,
    title text NOT NULL,
    volume int AUTO_INCREMENT UNIQUE KEY,
    categoryName varchar(100) NOT NULL UNIQUE,
    series_id int NULL
);

CREATE TABLE stocks(
    categoryid int NOT NULL,
    pieceid int NOT NULL,
    categoryName varchar(100) NOT NULL,
    piecesAvailable int DEFAULT (0),
    piecesRequired int DEFAULT (0) NOT NULL,
    PRIMARY KEY(categoryid, pieceid)
);

CREATE TABLE stocks_price(
    categoryid int NOT NULL,
    pieceid int NOT NULL,
    instant varchar(10) NOT NULL,
    price double,
    is_wholesale_price boolean,
    PRIMARY KEY(categoryid, pieceid, instant)
);

CREATE TABLE brokers(
    `ID Number` int PRIMARY KEY,
    `First Name` text NOT NULL,
    `Last Name` text NOT NULL
);

CREATE TABLE type_table(
    id int AUTO_INCREMENT PRIMARY KEY,
    byte_types tinyint unsigned,
    short_types smallint,
    int_types int,
    long_types bigint,
    string_types text,
    single_types float,
    float_types double,
    decimal_types decimal,
    boolean_types boolean,
    datetime_types datetime,
    bytearray_types blob
);

CREATE TABLE trees (
    treeId int PRIMARY KEY,
    species text,
    region text,
    height text
);

CREATE TABLE fungi (
    speciesid int PRIMARY KEY,
    region text
);

CREATE TABLE empty_table (
    id int PRIMARY KEY
);

CREATE TABLE notebooks (
    id int PRIMARY KEY,
    notebookname text,
    color text,
    ownername text
);

CREATE TABLE journals (
    id int PRIMARY KEY,
    journalname text,
    color text,
    ownername text
);

CREATE TABLE aow (
    NoteNum int PRIMARY KEY,
    DetailAssessmentAndPlanning text,
    WagingWar text,
    StrategicAttack text
);

CREATE TABLE series (
    id int AUTO_INCREMENT PRIMARY KEY,
    name text NOT NULL
);

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

INSERT INTO publishers(id, name) VALUES (1234, 'Big Company'), (2345, 'Small Town Publisher'), (2323, 'TBD Publishing One'), (2324, 'TBD Publishing Two Ltd'), (1940, 'Policy Publisher 01'), (1941, 'Policy Publisher 02');
INSERT INTO authors(id, name, birthdate) VALUES (123, 'Jelte', '2001-01-01'), (124, 'Aniruddh', '2002-02-02'), (125, 'Aniruddh', '2001-01-01'), (126, 'Aaron', '2001-01-01');
INSERT INTO books(id, title, publisher_id) VALUES (1, 'Awesome book', 1234), (2, 'Also Awesome book', 1234), (3, 'Great wall of china explained', 2345), (4, 'US history in a nutshell', 2345), (5, 'Chernobyl Diaries', 2323), (6, 'The Palace Door', 2324), (7, 'The Groovy Bar', 2324), (8, 'Time to Eat', 2324), (9, 'Policy-Test-01', 1940), (10, 'Policy-Test-02', 1940), (11, 'Policy-Test-04', 1941), (12, 'Time to Eat 2', 1941);
INSERT INTO book_website_placements(book_id, price) VALUES (1, 100), (2, 50), (3, 23), (5, 33);
INSERT INTO website_users(id, username) VALUES (1, 'George'), (2, NULL), (3, ''), (4, 'book_lover_95'), (5, 'null');
INSERT INTO book_author_link(book_id, author_id) VALUES (1, 123), (2, 124), (3, 123), (3, 124), (4, 123), (4, 124);
INSERT INTO reviews(id, book_id, content) VALUES (567, 1, 'Indeed a great book'), (568, 1, 'I loved it'), (569, 1, 'best book I read in years');
INSERT INTO magazines(id, title, issue_number) VALUES (1, 'Vogue', 1234), (11, 'Sports Illustrated', NULL), (3, 'Fitness', NULL);
INSERT INTO series(id, name) VALUES (3001, 'Foundation'), (3002, 'Hyperion Cantos');
INSERT INTO comics(id, title, categoryName, series_id) 
VALUES (1, 'Star Trek', 'SciFi', NULL), (2, 'Cinderella', 'FairyTales', 3001), (3,'Únknown','', 3002), (4, 'Alexander the Great', 'Historical', NULL);
INSERT INTO stocks(categoryid, pieceid, categoryName) VALUES (1, 1, 'SciFi'), (2, 1, 'FairyTales'),(0,1,''),(100, 99, 'Historical');
INSERT INTO brokers(`ID Number`, `First Name`, `Last Name`) VALUES (1, 'Michael', 'Burry'), (2, 'Jordan', 'Belfort');
INSERT INTO stocks_price(categoryid, pieceid, instant, price, is_wholesale_price) VALUES (2, 1, 'instant1', 100.57, true), (1, 1, 'instant2', 42.75, false);
INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, single_types, float_types, decimal_types, boolean_types, datetime_types, bytearray_types) VALUES
    (1, 1, 1, 1, 1, '', 0.33, 0.33, 0.333333, true, '1999-01-08 10:23:54', 0xABCDEF0123),
    (2, 0, -1, -1, -1, 'lksa;jdflasdf;alsdflksdfkldj', -9.2, -9.2, -9.292929, false, '1999-01-08 10:23:00', 0x98AB7511AABB1234),
    (3, 0, -32768, -2147483648, -9223372036854775808, '', -3.4E38, -1.7E308, 2.929292E-100, true, '1999-01-08 10:23:00', 0xFFFFFFFF),
    (4, 255, 32767, 2147483647, 9223372036854775807, 'null', 3.4E38, 1.7E308, 2.929292E-100, true, '1999-01-08 10:23:00', 0xFFFFFFFF),
    (5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);
INSERT INTO trees(treeId, species, region, height) VALUES (1, 'Tsuga terophylla', 'Pacific Northwest', '30m'), (2, 'Pseudotsuga menziesii', 'Pacific Northwest', '40m');
INSERT INTO fungi(speciesid, region) VALUES (1, 'northeast'), (2, 'southwest');
INSERT INTO notebooks(id, notebookname, color, ownername) VALUES (1, 'Notebook1', 'red', 'Sean'), (2, 'Notebook2', 'green', 'Ani'), (3, 'Notebook3', 'blue', 'Jarupat'), (4, 'Notebook4', 'yellow', 'Aaron');
INSERT INTO journals(id, journalname, color, ownername) VALUES (1, 'Journal1', 'red', 'Sean'), (2, 'Journal2', 'green', 'Ani'), (3, 'Journal3', 'blue', 'Jarupat'), (4, 'Journal4', 'yellow', 'Aaron');

INSERT INTO aow(NoteNum, DetailAssessmentAndPlanning, WagingWar, StrategicAttack) VALUES (1, 'chapter one notes: ', 'chapter two notes: ', 'chapter three notes: ');
-- Starting with id > 5000 is chosen arbitrarily so that the incremented id-s won't conflict with the manually inserted ids in this script
-- AUTO_INCREMENT is set to 5001 so the next autogenerated id will be 5001

ALTER TABLE books AUTO_INCREMENT = 5001;
ALTER TABLE book_website_placements AUTO_INCREMENT = 5001;
ALTER TABLE publishers AUTO_INCREMENT = 5001;
ALTER TABLE authors AUTO_INCREMENT = 5001;
ALTER TABLE reviews AUTO_INCREMENT = 5001;
ALTER TABLE comics AUTO_INCREMENT = 5001;
ALTER TABLE type_table AUTO_INCREMENT = 5001;

prepare stmt1 from  'CREATE VIEW books_view_all AS SELECT * FROM books';

prepare stmt2 from 'CREATE VIEW stocks_view_selected AS SELECT
                    categoryid,pieceid,categoryName,piecesAvailable
                    FROM stocks';

prepare stmt3 from 'CREATE VIEW books_publishers_view_composite as SELECT
                    publishers.name,books.id,publishers.id as publisher_id
                    FROM books,publishers
                    where publishers.id = books.publisher_id';

execute stmt1;
execute stmt2;
execute stmt3;
