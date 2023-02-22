-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

DROP TABLE IF EXISTS books_authors;
DROP TABLE IF EXISTS  books;
DROP TABLE IF EXISTS  authors;
DROP SEQUENCE IF EXISTS  globalId;

CREATE SEQUENCE globalId
    AS int
    INCREMENT by 1
    MINVALUE 1000000
;

CREATE TABLE books
(
    id INT NOT NULL PRIMARY KEY DEFAULT nextval('globalId'),
    title VARCHAR(1000) NOT NULL,
    year int NULL,
    pages int NULL
)
;

CREATE TABLE authors
(
    id INT NOT NULL PRIMARY KEY DEFAULT nextval('globalId'),
    first_name VARCHAR(100) NOT NULL,
    middle_name  VARCHAR(100) NULL,
    last_name VARCHAR(100) NOT NULL
)
;

CREATE TABLE books_authors
(
    author_id INT NOT NULL REFERENCES authors(id),
    book_id INT NOT NULL REFERENCES books(id),
    PRIMARY KEY (author_id,book_id)
)
;


CREATE INDEX ixnc1 on books_authors(book_id, author_id)
;

INSERT INTO authors VALUES
    (1, 'Isaac', null, 'Asimov'),
    (2, 'Robert', 'A.', 'Heinlein'),
    (3, 'Robert', null, 'Silvenberg'),
    (4, 'Dan', null, 'Simmons'),
    (5, 'Davide', null, 'Mauri'),
    (6, 'Bob', null, 'Ward'),
    (7, 'Anna', null, 'Hoffman'),
    (8, 'Silvano', null, 'Coriani'),
    (9, 'Sanjay', null, 'Mishra'),
    (10, 'Jovan', null, 'Popovic')
;

INSERT INTO books VALUES
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
    (1016, 'The Rise of Endymion', 1997, 579),
    (1017, 'Practical Azure SQL Database for Modern Developers', 2020, 326),
    (1018, 'SQL Server 2019 Revealed: Including Big Data Clusters and Machine Learning', 2019, 444),
    (1019, 'Azure SQL Revealed: A Guide to the Cloud for SQL Server Professionals', 2020, 528),
    (1020, 'SQL Server 2022 Revealed: A Hybrid Data Platform Powered by Security, Performance, and Availability', 2022, 506)
;


INSERT INTO books_authors VALUES
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
    (4, 1016),
    (5, 1017),
    (6, 1018),
    (6, 1019),
    (6, 1020),
    (7, 1017), 
    (8, 1017), 
    (9, 1017), 
    (10, 1017)
;

CREATE OR REPLACE VIEW books_details
AS
    WITH aggregated_authors AS 
    (
        SELECT 
            ba.book_id,
            STRING_AGG (CONCAT(a.first_name, ' ', a.middle_name ,' ', a.last_name) , ', ') as authors
        FROM
            books_authors ba 
        INNER JOIN
            authors a on ba.author_id = a.id
        GROUP BY
            ba.book_id    
    )
    SELECT
        b.id,
        b.title,
        b.pages,
        b.year,
        aa.authors
    FROM
        books b
    INNER JOIN
        aggregated_authors aa ON b.id = aa.book_id
    ;
