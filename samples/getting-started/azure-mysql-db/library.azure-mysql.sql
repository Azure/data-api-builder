-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

CREATE DATABASE  IF NOT EXISTS `booksdb` 
USE `booksdb`;

--
-- Table structure for table `authors`
--

DROP TABLE IF EXISTS `authors`;

 
CREATE TABLE `authors` (
  `id` int(11) NOT NULL,
  `first_name` varchar(100) CHARACTER SET utf8 NOT NULL,
  `middle_name` varchar(100) CHARACTER SET utf8 DEFAULT NULL,
  `last_name` varchar(100) CHARACTER SET utf8 NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `authors`
--

LOCK TABLES `authors` WRITE;
 
INSERT INTO `authors` VALUES (1,'Isaac',NULL,'Asimov'),(2,'Robert','A.','Heinlein'),(3,'Robert',NULL,'Silvenberg'),(4,'Dan',NULL,'Simmons'),(5,'Davide',NULL,'Mauri'),(6,'Bob',NULL,'Ward'),(7,'Anna',NULL,'Hoffman'),(8,'Silvano',NULL,'Coriani'),(9,'Sanjay',NULL,'Mishra'),(10,'Jovan',NULL,'Popovic');
 
UNLOCK TABLES;

--
-- Table structure for table `books`
--

DROP TABLE IF EXISTS `books`;

 
CREATE TABLE `books` (
  `id` int(11) NOT NULL,
  `title` varchar(1000) CHARACTER SET utf8 NOT NULL,
  `year` int(11) DEFAULT NULL,
  `pages` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

--
-- Dumping data for table `books`
--

LOCK TABLES `books` WRITE;

INSERT INTO `books` VALUES (1000,'Prelude to Foundation',1988,403),(1001,'Forward the Foundation',1993,417),(1002,'Foundation',1951,255),(1003,'Foundation and Empire',1952,247),(1004,'Second Foundation',1953,210),(1005,'Foundation\'s Edge',1982,367),(1006,'Foundation and Earth',1986,356),(1007,'Nemesis',1989,386),(1008,'Starship Troopers',NULL,NULL),(1009,'Stranger in a Strange Land',NULL,NULL),(1010,'Nightfall',NULL,NULL),(1011,'Nightwings',NULL,NULL),(1012,'Across a Billion Years',NULL,NULL),(1013,'Hyperion',1989,482),(1014,'The Fall of Hyperion',1990,517),(1015,'Endymion',1996,441),(1016,'The Rise of Endymion',1997,579),(1017,'Practical Azure SQL Database for Modern Developers',2020,326),(1018,'SQL Server 2019 Revealed: Including Big Data Clusters and Machine Learning',2019,444),(1019,'Azure SQL Revealed: A Guide to the Cloud for SQL Server Professionals',2020,528),(1020,'SQL Server 2022 Revealed: A Hybrid Data Platform Powered by Security, Performance, and Availability',2022,506);

UNLOCK TABLES;

--
-- Table structure for table `books_authors`
--

DROP TABLE IF EXISTS `books_authors`;

 
CREATE TABLE `books_authors` (
  `author_id` int(11) NOT NULL,
  `book_id` int(11) NOT NULL,
  KEY `author_id` (`author_id`),
  KEY `book_id` (`book_id`),
  CONSTRAINT `books_authors_ibfk_1` FOREIGN KEY (`author_id`) REFERENCES `authors` (`id`),
  CONSTRAINT `books_authors_ibfk_2` FOREIGN KEY (`book_id`) REFERENCES `books` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `books_authors`
--

LOCK TABLES `books_authors` WRITE;

INSERT INTO `books_authors` VALUES (1,1000),(1,1001),(1,1002),(1,1003),(1,1004),(1,1005),(1,1006),(1,1007),(1,1010),(2,1008),(2,1009),(2,1011),(3,1010),(3,1012),(4,1013),(4,1014),(4,1015),(4,1016),(5,1017),(6,1018),(6,1019),(6,1020),(7,1017),(8,1017),(9,1017),(10,1017);

UNLOCK TABLES;
