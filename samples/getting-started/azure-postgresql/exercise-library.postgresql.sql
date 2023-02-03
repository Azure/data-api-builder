DROP TABLE IF EXISTS series
;

CREATE TABLE series
(
    id INT PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
    name VARCHAR(1000) NOT NULL
)
;


ALTER TABLE books 
    ADD series_id INT NULL
;


ALTER TABLE books 
    ADD FOREIGN KEY (series_id) REFERENCES series(id)
;

INSERT INTO series 
OVERRIDING SYSTEM VALUE
VALUES
    (10000, 'Foundation'),
    (10001, 'Hyperion Cantos')

;

UPDATE books 
SET series_id = 10000
WHERE id IN (1000, 1001, 1002, 1003, 1004, 1005, 1006)
;

UPDATE books 
SET series_id = 10001
WHERE id IN (1013, 1014, 1015, 1016)
;

