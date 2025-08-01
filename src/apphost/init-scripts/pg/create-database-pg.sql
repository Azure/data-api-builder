-- create-database-pg.sql
-- PostgreSQL version of create-database.sql

-- Create database (run this as a superuser, outside the target database)
-- Uncomment and edit the database name as needed
-- CREATE DATABASE "Trek"
--     WITH 
--     OWNER = trek_user
--     ENCODING = 'UTF8'
--     LC_COLLATE = 'en_US.utf8'
--     LC_CTYPE = 'en_US.utf8'
--     TEMPLATE = template0;

-- Connect to the target database before running the rest of the script

-- Drop tables in reverse order of creation due to foreign key dependencies
DROP TABLE IF EXISTS Character_Species;
DROP TABLE IF EXISTS Series_Character;
DROP TABLE IF EXISTS "Character";
DROP TABLE IF EXISTS Species;
DROP TABLE IF EXISTS Actor;
DROP TABLE IF EXISTS Series;

-- create tables
CREATE TABLE Series (
    Id INTEGER PRIMARY KEY,
    Name VARCHAR(255) NOT NULL
);

CREATE TABLE Actor (
    Id INTEGER PRIMARY KEY,
    Name VARCHAR(255) NOT NULL,
    "BirthYear" INTEGER NOT NULL
);

CREATE TABLE Species (
    Id INTEGER PRIMARY KEY,
    Name VARCHAR(255) NOT NULL
);

CREATE TABLE "Character" (
    Id INTEGER PRIMARY KEY,
    Name VARCHAR(255) NOT NULL,
    ActorId INTEGER NOT NULL,
    Stardate DECIMAL(10, 2),
    FOREIGN KEY (ActorId) REFERENCES Actor(Id)
);

CREATE TABLE Series_Character (
    SeriesId INTEGER,
    CharacterId INTEGER,
    Role VARCHAR(500),
    FOREIGN KEY (SeriesId) REFERENCES Series(Id),
    FOREIGN KEY (CharacterId) REFERENCES "Character"(Id),
    PRIMARY KEY (SeriesId, CharacterId)
);

CREATE TABLE Character_Species (
    CharacterId INTEGER,
    SpeciesId INTEGER,
    FOREIGN KEY (CharacterId) REFERENCES "Character"(Id),
    FOREIGN KEY (SpeciesId) REFERENCES Species(Id),
    PRIMARY KEY (CharacterId, SpeciesId)
);

-- create data
INSERT INTO Series (Id, Name) VALUES 
    (1, 'Star Trek'),
    (2, 'Star Trek: The Next Generation'),
    (3, 'Star Trek: Voyager'),
    (4, 'Star Trek: Deep Space Nine'),
    (5, 'Star Trek: Enterprise');

INSERT INTO Species (Id, Name) VALUES 
    (1, 'Human'),
    (2, 'Vulcan'),
    (3, 'Android'),
    (4, 'Klingon'),
    (5, 'Betazoid'),
    (6, 'Hologram'),
    (7, 'Bajoran'),
    (8, 'Changeling'),
    (9, 'Trill'),
    (10, 'Ferengi'),
    (11, 'Denobulan'),
    (12, 'Borg');

INSERT INTO Actor (Id, Name, "BirthYear") VALUES 
    (1, 'William Shatner', 1931),
    (2, 'Leonard Nimoy', 1931),
    (3, 'DeForest Kelley', 1920),
    (4, 'James Doohan', 1920),
    (5, 'Nichelle Nichols', 1932),
    (6, 'George Takei', 1937),
    (7, 'Walter Koenig', 1936),
    (8, 'Patrick Stewart', 1940),
    (9, 'Jonathan Frakes', 1952),
    (10, 'Brent Spiner', 1949),
    (11, 'Michael Dorn', 1952),
    (12, 'Gates McFadden', 1949),
    (13, 'Marina Sirtis', 1955),
    (14, 'LeVar Burton', 1957),
    (15, 'Kate Mulgrew', 1955),
    (16, 'Robert Beltran', 1953),
    (17, 'Tim Russ', 1956),
    (18, 'Roxann Dawson', 1958),
    (19, 'Robert Duncan McNeill', 1964),
    (20, 'Garrett Wang', 1968),
    (21, 'Robert Picardo', 1953),
    (22, 'Jeri Ryan', 1968),
    (23, 'Avery Brooks', 1948),
    (24, 'Nana Visitor', 1957),
    (25, 'Rene Auberjonois', 1940),
    (26, 'Terry Farrell', 1963),
    (27, 'Alexander Siddig', 1965),
    (28, 'Armin Shimerman', 1949),
    (29, 'Cirroc Lofton', 1978),
    (30, 'Scott Bakula', 1954),
    (31, 'Jolene Blalock', 1975),
    (32, 'John Billingsley', 1960),
    (33, 'Connor Trinneer', 1969),
    (34, 'Dominic Keating', 1962),
    (35, 'Linda Park', 1978),
    (36, 'Anthony Montgomery', 1971);

INSERT INTO "Character" (Id, Name, ActorId, Stardate) VALUES 
    (1, 'James T. Kirk', 1, 2233.04),
    (2, 'Spock', 2, 2230.06),
    (3, 'Leonard McCoy', 3, 2227.00),
    (4, 'Montgomery Scott', 4, 2222.00),
    (5, 'Uhura', 5, 2233.00),
    (6, 'Hikaru Sulu', 6, 2237.00),
    (7, 'Pavel Chekov', 7, 2245.00),
    (8, 'Jean-Luc Picard', 8, 2305.07),
    (9, 'William Riker', 9, 2335.08),
    (10, 'Data', 10, 2336.00),
    (11, 'Worf', 11, 2340.00),
    (12, 'Beverly Crusher', 12, 2324.00),
    (13, 'Deanna Troi', 13, 2336.00),
    (14, 'Geordi La Forge', 14, 2335.02),
    (15, 'Kathryn Janeway', 15, 2336.05),
    (16, 'Chakotay', 16, 2329.00),
    (17, 'Tuvok', 17, 2264.00),
    (18, 'B''Elanna Torres', 18, 2349.00),
    (19, 'Tom Paris', 19, 2346.00),
    (20, 'Harry Kim', 20, 2349.00),
    (21, 'The Doctor', 21, 2371.00), -- Stardate of activation
    (22, 'Seven of Nine', 22, 2348.00),
    (23, 'Benjamin Sisko', 23, 2332.00),
    (24, 'Kira Nerys', 24, 2343.00),
    (25, 'Odo', 25, 2337.00), -- Approximate stardate of discovery
    (27, 'Jadzia Dax', 26, 2341.00),
    (28, 'Julian Bashir', 27, 2341.00),
    (29, 'Quark', 28, 2333.00),
    (30, 'Jake Sisko', 29, 2355.00),
    (31, 'Jonathan Archer', 30, 2112.00),
    (32, 'T''Pol', 31, 2088.00),
    (33, 'Phlox', 32, 2102.00),
    (34, 'Charles "Trip" Tucker III', 33, 2121.00),
    (35, 'Malcolm Reed', 34, 2117.00),
    (36, 'Hoshi Sato', 35, 2129.00),
    (37, 'Travis Mayweather', 36, 2126.00);

INSERT INTO Series_Character (SeriesId, CharacterId, Role) VALUES 
    (1, 1, 'Captain'), -- James T. Kirk in Star Trek
    (1, 2, 'Science Officer'), -- Spock in Star Trek
    (1, 3, 'Doctor'), -- Leonard McCoy in Star Trek
    (1, 4, 'Engineer'), -- Montgomery Scott in Star Trek
    (1, 5, 'Communications Officer'), -- Uhura in Star Trek
    (1, 6, 'Helmsman'), -- Hikaru Sulu in Star Trek
    (1, 7, 'Navigator'), -- Pavel Chekov in Star Trek
    (2, 8, 'Captain'), -- Jean-Luc Picard in Star Trek: The Next Generation
    (2, 9, 'First Officer'), -- William Riker in Star Trek: The Next Generation
    (2, 10, 'Operations Officer'),-- Data in Star Trek: The Next Generation
    (2, 11, 'Security Officer'),-- Worf in Star Trek: The Next Generation
    (2, 12, 'Doctor'),-- Beverly Crusher in Star Trek: The Next Generation
    (2, 13, 'Counselor'),-- Deanna Troi in Star Trek: The Next Generation
    (2, 14, 'Engineer'),-- Geordi La Forge in Star Trek: The Next Generation
    (3, 15, 'Captain'),-- Kathryn Janeway in Star Trek: Voyager
    (3, 16, 'First Officer'),-- Chakotay in Star Trek: Voyager
    (3, 17, 'Tactical Officer'),-- Tuvok in Star Trek: Voyager
    (3, 18, 'Engineer'),-- B'Elanna Torres in Star Trek: Voyager
    (3, 19, 'Helmsman'),-- Tom Paris in Star Trek: Voyager
    (3, 20, 'Operations Officer'),-- Harry Kim in Star Trek: Voyager
    (3, 21, 'Doctor'),-- The Doctor in Star Trek: Voyager
    (3, 22, 'Astrometrics Officer'),-- Seven of Nine in Star Trek: Voyager
    (4, 23, 'Commanding Officer'),-- Benjamin Sisko in Star Trek: Deep Space Nine
    (4, 24, 'First Officer'),-- Kira Nerys in Star Trek: Deep Space Nine
    (4, 25, 'Security Officer'),-- Odo in Star Trek: Deep Space Nine
    (4, 11, 'Strategic Operations Officer'),-- Worf in Star Trek: Deep Space Nine
    (4, 27, 'Science Officer'),-- Jadzia Dax in Star Trek: Deep Space Nine
    (4, 28, 'Doctor'),-- Julian Bashir in Star Trek: Deep Space Nine
    (4, 29, 'Bar Owner'),-- Quark in Star Trek: Deep Space Nine
    (4, 30, 'Civilian'),-- Jake Sisko in Star Trek: Deep Space Nine
    (5, 31, 'Captain'),-- Jonathan Archer in Star Trek: Enterprise
    (5, 32, 'Science Officer'),-- T'Pol in Star Trek: Enterprise
    (5, 33, 'Doctor'),-- Phlox in Star Trek: Enterprise
    (5, 34, 'Chief Engineer'),-- Charles "Trip" Tucker III in Star Trek: Enterprise
    (5, 35, 'Armory Officer'),-- Malcolm Reed in Star Trek: Enterprise
    (5, 36, 'Communications Officer'),-- Hoshi Sato in Star Trek: Enterprise
    (5, 37, 'Helmsman');-- Travis Mayweather in Star Trek: Enterprise

INSERT INTO Character_Species (CharacterId, SpeciesId) VALUES 
    (1, 1),  -- James T. Kirk is Human
    (2, 2),  -- Spock is Vulcan
    (2, 1),  -- Spock is also Human
    (3, 1),  -- Leonard McCoy is Human
    (4, 1),  -- Montgomery Scott is Human
    (5, 1),  -- Uhura is Human
    (6, 1),  -- Hikaru Sulu is Human
    (7, 1),  -- Pavel Chekov is Human
    (8, 1),  -- Jean-Luc Picard is Human
    (9, 1),  -- William Riker is Human
    (10, 3), -- Data is Android
    (11, 4), -- Worf is Klingon
    (12, 1), -- Beverly Crusher is Human
    (13, 1), -- Deanna Troi is Human
    (13, 5), -- Deanna Troi is also Betazoid
    (14, 1), -- Geordi La Forge is Human
    (15, 1), -- Kathryn Janeway is Human
    (16, 1), -- Chakotay is Human
    (17, 2), -- Tuvok is Vulcan
    (18, 1), -- B'Elanna Torres is Human
    (18, 4), -- B'Elanna Torres is also Klingon
    (19, 1), -- Tom Paris is Human
    (20, 1), -- Harry Kim is Human
    (21, 6), -- The Doctor is a Hologram
    (22, 1), -- Seven of Nine is Human
    (22, 12),-- Seven of Nine is also Borg
    (23, 1), -- Benjamin Sisko is Human
    (24, 7), -- Kira Nerys is Bajoran
    (25, 8), -- Odo is Changeling
    (27, 9), -- Jadzia Dax is Trill
    (28, 1), -- Julian Bashir is Human
    (29, 10),-- Quark is Ferengi
    (30, 1), -- Jake Sisko is Human
    (31, 1), -- Jonathan Archer is Human
    (32, 2), -- T'Pol is Vulcan
    (33, 11),-- Phlox is Denobulan
    (34, 1), -- Charles "Trip" Tucker III is Human
    (35, 1), -- Malcolm Reed is Human
    (36, 1), -- Hoshi Sato is Human
    (37, 1); -- Travis Mayweather is Human