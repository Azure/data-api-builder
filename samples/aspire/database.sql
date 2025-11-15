USE [Master]

GO
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'StarTrek')
BEGIN
    CREATE DATABASE StarTrek;
END

GO
USE [StarTrek]

GO
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Actor')
BEGIN
    RETURN; -- assume all tables exist
END

GO
-- Drop tables in reverse order of creation due to foreign key dependencies
DROP TABLE IF EXISTS Character_Species;
DROP TABLE IF EXISTS Series_Character;
DROP TABLE IF EXISTS Character;
DROP TABLE IF EXISTS Species;
DROP TABLE IF EXISTS Actor;
DROP TABLE IF EXISTS Series;
DROP VIEW IF EXISTS SeriesActors;
DROP PROC IF EXISTS GetSeriesActors;

GO
-- create tables
CREATE TABLE Series (
    Id INT PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL
);

GO
CREATE TABLE Actor (
    Id INT PRIMARY KEY,
    FirstName NVARCHAR(100) NOT NULL,
    LastName NVARCHAR(100) NOT NULL,
    [BirthYear] INT NOT NULL,
    FullName AS (RTRIM(LTRIM(FirstName + ' ' + LastName))) PERSISTED
);

GO
CREATE TABLE Species (
    Id INT PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL
);

GO
CREATE TABLE Character (
    Id INT PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    ActorId INT NOT NULL,
    Stardate DECIMAL(10, 2),
    FOREIGN KEY (ActorId) REFERENCES Actor(Id)
);

GO
CREATE TABLE Series_Character (
    SeriesId INT,
    CharacterId INT,
    Role VARCHAR(500),
    FOREIGN KEY (SeriesId) REFERENCES Series(Id),
    FOREIGN KEY (CharacterId) REFERENCES Character(Id),
    PRIMARY KEY (SeriesId, CharacterId)
);

GO
CREATE TABLE Character_Species (
    CharacterId INT,
    SpeciesId INT,
    FOREIGN KEY (CharacterId) REFERENCES Character(Id),
    FOREIGN KEY (SpeciesId) REFERENCES Species(Id),
    PRIMARY KEY (CharacterId, SpeciesId)
);

GO
-- create data
INSERT INTO Series (Id, Name) VALUES 
    (1, 'Star Trek'),
    (2, 'Star Trek: The Next Generation'),
    (3, 'Star Trek: Voyager'),
    (4, 'Star Trek: Deep Space Nine'),
    (5, 'Star Trek: Enterprise'),
    (6, 'Star Trek: Discovery'),
    (7, 'Star Trek: Strange New Worlds'),
    (8, 'Star Trek: Lower Decks'),
    (9, 'Star Trek: Prodigy'),
    (10, 'Star Trek: Starfleet Academy'),
    (11, 'Star Trek: Section 31');

GO
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
    (12, 'Borg'),
    (13, 'Kelpien'),
    (14, 'Illyrian'),
    (15, 'Brikar'),
    (16, 'Medusan'),
    (17, 'Vau N''Akat'),
    (18, 'Deltan'),
    (19, 'Chameloid'),
    (20, 'Nanokin');

GO
INSERT INTO Actor (Id, FirstName, LastName, [BirthYear]) VALUES 
    (1, 'William', 'Shatner', 1931),
    (2, 'Leonard', 'Nimoy', 1931),
    (3, 'DeForest', 'Kelley', 1920),
    (4, 'James', 'Doohan', 1920),
    (5, 'Nichelle', 'Nichols', 1932),
    (6, 'George', 'Takei', 1937),
    (7, 'Walter', 'Koenig', 1936),
    (8, 'Patrick', 'Stewart', 1940),
    (9, 'Jonathan', 'Frakes', 1952),
    (10, 'Brent', 'Spiner', 1949),
    (11, 'Michael', 'Dorn', 1952),
    (12, 'Gates', 'McFadden', 1949),
    (13, 'Marina', 'Sirtis', 1955),
    (14, 'LeVar', 'Burton', 1957),
    (15, 'Kate', 'Mulgrew', 1955),
    (16, 'Robert', 'Beltran', 1953),
    (17, 'Tim', 'Russ', 1956),
    (18, 'Roxann', 'Dawson', 1958),
    (19, 'Robert', 'Duncan McNeill', 1964),
    (20, 'Garrett', 'Wang', 1968),
    (21, 'Robert', 'Picardo', 1953),
    (22, 'Jeri', 'Ryan', 1968),
    (23, 'Avery', 'Brooks', 1948),
    (24, 'Nana', 'Visitor', 1957),
    (25, 'Rene', 'Auberjonois', 1940),
    (26, 'Terry', 'Farrell', 1963),
    (27, 'Alexander', 'Siddig', 1965),
    (28, 'Armin', 'Shimerman', 1949),
    (29, 'Cirroc', 'Lofton', 1978),
    (30, 'Scott', 'Bakula', 1954),
    (31, 'Jolene', 'Blalock', 1975),
    (32, 'John', 'Billingsley', 1960),
    (33, 'Connor', 'Trinneer', 1969),
    (34, 'Dominic', 'Keating', 1962),
    (35, 'Linda', 'Park', 1978),
    (36, 'Anthony', 'Montgomery', 1971),
    (37, 'Sonequa', 'Martin-Green', 1985),
    (38, 'Doug', 'Jones', 1960),
    (39, 'Anthony', 'Rapp', 1971),
    (40, 'Mary', 'Wiseman', 1985),
    (41, 'Wilson', 'Cruz', 1974),
    -- Strange New Worlds cast
    (42, 'Anson', 'Mount', 1973),
    (43, 'Ethan', 'Peck', 1986),
    (44, 'Rebecca', 'Romijn', 1972),
    (45, 'Celia', 'Rose Gooding', 1999),
    (46, 'Jess', 'Bush', 1991),
    -- Lower Decks cast
    (47, 'Tawny', 'Newsome', 1983),
    (48, 'Jack', 'Quaid', 1992),
    (49, 'Noel', 'Wells', 1986),
    (50, 'Eugene', 'Cordero', 1986),
    (51, 'Dawnn', 'Lewis', 1961),
    -- Prodigy cast
    (52, 'Brett', 'Gray', 1996),
    (53, 'Ella', 'Purnell', 1996),
    (54, 'Jason', 'Mantzoukas', 1972),
    (55, 'Angus', 'Imrie', 1994),
    (56, 'Dee Bradley', 'Baker', 1962),
    -- Starfleet Academy cast
    (57, 'Holly', 'Hunter', 1958),
    (58, 'Paul', 'Giamatti', 1967),
    (59, 'Kerrice', 'Brooks', 1990),
    (60, 'Bella', 'Shepard', 2005),
    (61, 'George', 'Hawkins', 1999),
    -- Section 31 cast
    (62, 'Michelle', 'Yeoh', 1962),
    (63, 'Omari', 'Hardwick', 1974),
    (64, 'Sam', 'Richardson', 1984),
    (65, 'Robert', 'Kazinsky', 1983),
    (66, 'Kacey', 'Rohl', 1991);

INSERT INTO Character (Id, Name, ActorId, Stardate) VALUES 
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
    (21, 'The Doctor', 21, 2371.00),
    (22, 'Seven of Nine', 22, 2348.00),
    (23, 'Benjamin Sisko', 23, 2332.00),
    (24, 'Kira Nerys', 24, 2343.00),
    (25, 'Odo', 25, 2337.00),
    (26, 'Ezri Dax', 26, 2368.00),
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
    (37, 'Travis Mayweather', 36, 2126.00),
    (38, 'Michael Burnham', 37, 2226.00),
    (39, 'Saru', 38, 2237.00),
    (40, 'Paul Stamets', 39, 2246.00),
    (41, 'Sylvia Tilly', 40, 2249.00),
    (42, 'Hugh Culber', 41, 2246.00),
    -- Strange New Worlds characters
    (43, 'Christopher Pike', 42, 2219.00),
    (44, 'Spock (SNW)', 43, 2230.06),
    (45, 'Una Chin-Riley', 44, 2220.00),
    (46, 'Nyota Uhura (SNW)', 45, 2233.00),
    (47, 'Christine Chapel', 46, 2230.00),
    -- Lower Decks characters
    (48, 'Beckett Mariner', 47, 2357.00),
    (49, 'Brad Boimler', 48, 2361.00),
    (50, 'D''Vana Tendi', 49, 2368.00),
    (51, 'Sam Rutherford', 50, 2359.00),
    (52, 'Carol Freeman', 51, 2337.00),
    -- Prodigy characters
    (53, 'Dal R''El', 52, 2369.00),
    (54, 'Gwyndala', 53, 2369.00),
    (55, 'Jankom Pog', 54, 2368.00),
    (56, 'Zero', 55, 2370.00),
    (57, 'Murf', 56, 2371.00),
    -- Starfleet Academy characters
    (58, 'Admiral Grace', 57, 2328.00),
    (59, 'Chancellor Vix', 58, 2335.00),
    (60, 'Nyota Uhura (Academy)', 59, 2233.00),
    (61, 'Cadet Sato', 60, 2377.00),
    (62, 'Cadet Thira Sidhu', 61, 2378.00),
    -- Section 31 characters
    (63, 'Philippa Georgiou (Section 31)', 62, 2202.00),
    (64, 'Alok Sahar', 63, 1970.00),
    (65, 'Quasi', 64, 2335.00),
    (66, 'Zeph', 65, 2330.00),
    (67, 'Rachel Garrett (Young)', 66, 2305.00);

INSERT INTO Series_Character (SeriesId, CharacterId, Role) VALUES
    -- Star Trek (Original Series)
    (1, 1, 'Captain'),
    (1, 2, 'First Officer/Science Officer'),
    (1, 3, 'Chief Medical Officer'),
    (1, 4, 'Chief Engineer'),
    (1, 5, 'Communications Officer'),
    (1, 6, 'Helmsman'),
    (1, 7, 'Navigator'),
    -- Star Trek: The Next Generation
    (2, 8, 'Captain'),
    (2, 9, 'First Officer'),
    (2, 10, 'Operations Officer'),
    (2, 11, 'Chief of Security/Tactical Officer'),
    (2, 12, 'Chief Medical Officer'),
    (2, 13, 'Ship''s Counselor'),
    (2, 14, 'Chief Engineer'),
    -- Star Trek: Voyager
    (3, 15, 'Captain'),
    (3, 16, 'First Officer'),
    (3, 17, 'Chief of Security/Tactical Officer'),
    (3, 18, 'Chief Engineer'),
    (3, 19, 'Helmsman'),
    (3, 20, 'Operations Officer'),
    (3, 21, 'Chief Medical Officer'),
    (3, 22, 'Astrometrics Officer'),
    -- Star Trek: Deep Space Nine
    (4, 23, 'Commanding Officer'),
    (4, 24, 'First Officer'),
    (4, 25, 'Chief of Security'),
    (4, 26, 'Science Officer'),
    (4, 27, 'Science Officer'),
    (4, 28, 'Chief Medical Officer'),
    (4, 29, 'Bar Owner'),
    (4, 30, 'Civilian'),
    (4, 11, 'Strategic Operations Officer'), -- Worf also served on DS9
    -- Star Trek: Enterprise
    (5, 31, 'Captain'),
    (5, 32, 'Science Officer/First Officer'),
    (5, 33, 'Chief Medical Officer'),
    (5, 34, 'Chief Engineer'),
    (5, 35, 'Armory Officer'),
    (5, 36, 'Communications Officer'),
    (5, 37, 'Helmsman'),
    -- Star Trek: Discovery
    (6, 38, 'Science Specialist/First Officer'),
    (6, 39, 'First Officer/Captain'),
    (6, 40, 'Chief Engineer'),
    (6, 41, 'Ensign/Cadet'),
    (6, 42, 'Chief Medical Officer'),
    -- Star Trek: Strange New Worlds
    (7, 43, 'Captain'),
    (7, 44, 'Science Officer'),
    (7, 45, 'First Officer'),
    (7, 46, 'Communications Officer'),
    (7, 47, 'Nurse'),
    -- Star Trek: Lower Decks
    (8, 48, 'Ensign'),
    (8, 49, 'Ensign'),
    (8, 50, 'Ensign'),
    (8, 51, 'Ensign'),
    (8, 52, 'Captain'),
    -- Star Trek: Prodigy
    (9, 53, 'Crew Member'),
    (9, 54, 'Crew Member'),
    (9, 55, 'Crew Member'),
    (9, 56, 'Crew Member'),
    (9, 57, 'Crew Member'),
    -- Star Trek: Starfleet Academy
    (10, 58, 'Academy Commandant'),
    (10, 59, 'Academy Chancellor'),
    (10, 60, 'Cadet/Communications Specialist'),
    (10, 61, 'First Year Cadet'),
    (10, 62, 'First Year Cadet'),
    -- Star Trek: Section 31
    (11, 63, 'Emperor/Section 31 Operative'),
    (11, 64, 'Section 31 Agent'),
    (11, 65, 'Section 31 Agent/Shapeshifter'),
    (11, 66, 'Section 31 Agent'),
    (11, 67, 'Starfleet Officer/Future Captain');

INSERT INTO Character_Species (CharacterId, SpeciesId) VALUES
    -- Original Series
    (1, 1),  -- Kirk: Human
    (2, 2),  -- Spock: Vulcan
    (2, 1),  -- Spock: Human (half-human)
    (3, 1),  -- McCoy: Human
    (4, 1),  -- Scott: Human
    (5, 1),  -- Uhura: Human
    (6, 1),  -- Sulu: Human
    (7, 1),  -- Chekov: Human
    -- Next Generation
    (8, 1),  -- Picard: Human
    (9, 1),  -- Riker: Human
    (10, 3), -- Data: Android
    (11, 4), -- Worf: Klingon
    (12, 1), -- Crusher: Human
    (13, 5), -- Troi: Betazoid
    (13, 1), -- Troi: Human (half-human)
    (14, 1), -- La Forge: Human
    -- Voyager
    (15, 1), -- Janeway: Human
    (16, 1), -- Chakotay: Human
    (17, 2), -- Tuvok: Vulcan
    (18, 4), -- Torres: Klingon
    (18, 1), -- Torres: Human (half-human)
    (19, 1), -- Paris: Human
    (20, 1), -- Kim: Human
    (21, 6), -- The Doctor: Hologram
    (22, 1), -- Seven of Nine: Human
    (22, 12), -- Seven of Nine: Borg (ex-Borg)
    -- Deep Space Nine
    (23, 1), -- Sisko: Human
    (24, 7), -- Kira: Bajoran
    (25, 8), -- Odo: Changeling
    (26, 1), -- Ezri Dax: Human
    (26, 9), -- Ezri Dax: Trill (joined)
    (27, 9), -- Jadzia Dax: Trill
    (28, 1), -- Bashir: Human
    (29, 10), -- Quark: Ferengi
    (30, 1), -- Jake Sisko: Human
    -- Enterprise
    (31, 1), -- Archer: Human
    (32, 2), -- T'Pol: Vulcan
    (33, 11), -- Phlox: Denobulan
    (34, 1), -- Tucker: Human
    (35, 1), -- Reed: Human
    (36, 1), -- Sato: Human
    (37, 1), -- Mayweather: Human
    -- Discovery
    (38, 1), -- Burnham: Human
    (39, 13), -- Saru: Kelpien
    (40, 1), -- Stamets: Human
    (41, 1), -- Tilly: Human
    (42, 1), -- Culber: Human
    -- Strange New Worlds
    (43, 1), -- Pike: Human
    (44, 2), -- Spock: Vulcan
    (44, 1), -- Spock: Human (half-human)
    (45, 1), -- Una: Human
    (45, 14), -- Una: Illyrian (augmented)
    (46, 1), -- Uhura: Human
    (47, 1), -- Chapel: Human
    -- Lower Decks
    (48, 1), -- Mariner: Human
    (49, 1), -- Boimler: Human
    (50, 1), -- Tendi: Human (Orion)
    (51, 1), -- Rutherford: Human
    (52, 1), -- Freeman: Human
    -- Prodigy
    (53, 1), -- Dal: Human (augment)
    (54, 17), -- Gwyndala: Vau N'Akat
    (55, 1), -- Jankom Pog: Human (Tellarite)
    (56, 16), -- Zero: Medusan
    (57, 1), -- Murf: Unknown (listed as Human for database purposes)
    -- Starfleet Academy
    (58, 1), -- Admiral Grace: Human
    (59, 1), -- Chancellor Vix: Human
    (60, 1), -- Uhura (Academy): Human
    (61, 1), -- Sato: Human
    (62, 1), -- Thira Sidhu: Human
    -- Section 31
    (63, 1), -- Philippa Georgiou: Human
    (64, 1), -- Alok Sahar: Human
    (65, 19), -- Quasi: Chameloid
    (66, 1), -- Zeph: Human
    (67, 1); -- Rachel Garrett: Human

GO
CREATE VIEW [dbo].[SeriesActors]
AS
SELECT
    a.Id AS Id,
    a.FullName AS Actor,
    a.BirthYear AS BirthYear,
    s.Id AS SeriesId,
    s.Name AS Series
FROM Series s
JOIN Series_Character AS sc ON s.Id = sc.SeriesId
JOIN Character AS c ON sc.CharacterId = c.Id
JOIN Actor AS a ON c.ActorId = a.Id;

GO
CREATE PROCEDURE [dbo].[GetSeriesActors]
    @seriesId INT = 1,
    @top INT = 5
AS
SET NOCOUNT ON;
SELECT TOP (@top) * 
FROM SeriesActors
WHERE SeriesId = @seriesId;

GO
-- demo indexes for performance
CREATE INDEX IX_Character_ActorId ON Character(ActorId);

GO
CREATE INDEX IX_SeriesCharacter_SeriesId ON Series_Character(SeriesId);

GO
CREATE INDEX IX_CharacterSpecies_CharacterId ON Character_Species(CharacterId);
