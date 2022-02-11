DROP TABLE IF EXISTS book_author_link;
DROP TABLE IF EXISTS reviews;
DROP TABLE IF EXISTS authors;
DROP TABLE IF EXISTS books;
DROP TABLE IF EXISTS publishers;
CREATE TABLE publishers(
    id bigint AUTO_INCREMENT PRIMARY KEY,
    name text NOT NULL
);

CREATE TABLE books(
    id bigint AUTO_INCREMENT PRIMARY KEY,
    title text NOT NULL,
    publisher_id bigint NOT NULL
);

CREATE TABLE authors(
    id bigint AUTO_INCREMENT PRIMARY KEY,
    name text NOT NULL,
    birthdate text NOT NULL
);


CREATE TABLE reviews(
    book_id bigint NOT NULL,
    id bigint AUTO_INCREMENT,
    content text NOT NULL,
    PRIMARY KEY(book_id, id),
    INDEX (id)
);

CREATE TABLE book_author_link(
    book_id bigint NOT NULL,
    author_id bigint NOT NULL,
    PRIMARY KEY(book_id, author_id)
);

ALTER TABLE books
ADD CONSTRAINT book_publisher_fk
FOREIGN KEY (publisher_id)
REFERENCES publishers (id)
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

INSERT INTO publishers(id, name) VALUES (1234, 'Big Company'), (2345, 'Small Town Publisher'), (2323, 'TBD Publishing One'), (2324, 'TBD Publishing Two Ltd');
INSERT INTO authors(id, name, birthdate) VALUES (123, 'Jelte', '2001-01-01'), (124, 'Aniruddh', '2002-02-02');
INSERT INTO books(id, title, publisher_id) VALUES (1, 'Awesome book', 1234), (2, 'Also Awesome book', 1234), (3, 'Great wall of china explained', 2345), (4, 'US history in a nutshell', 2345), (5, 'Chernobyl Diaries', 2323), (6, 'The Palace Door', 2324);
INSERT INTO book_author_link(book_id, author_id) VALUES (1, 123), (2, 124), (3, 123), (3, 124), (4, 123), (4, 124);
INSERT INTO reviews(id, book_id, content) VALUES (567, 1, 'Indeed a great book'), (568, 1, 'I loved it'), (569, 1, 'best book I read in years');

-- Starting with id > 5000 is chosen arbitrarily so that the incremented id-s won't conflict with the manually inserted ids in this script
-- AUTO_INCREMENT is set to 5001 so the next autogenerated id will be 5001

ALTER TABLE books AUTO_INCREMENT = 5001;
ALTER TABLE publishers AUTO_INCREMENT = 5001;
ALTER TABLE authors AUTO_INCREMENT = 5001;
ALTER TABLE reviews AUTO_INCREMENT = 5001;


