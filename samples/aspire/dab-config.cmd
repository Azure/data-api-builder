@echo off

del /q dab-config.json 2>nul

set MSSQL_CONNECTION_STRING=%MSSQL_CONNECTION_STRING%

dab init --database-type mssql --connection-string "@env('MSSQL_CONNECTION_STRING')" --config dab-config.json --host-mode development

dab add Series --source dbo.Series --source.type table --permissions "anonymous:*" --description "Star Trek series"
dab update Series --fields.name Id --fields.description "Primary key" --fields.primary-key true
dab update Series --fields.name Name --fields.description "Series name" --fields.primary-key false

dab add Actor --source dbo.Actor --source.type table --permissions "anonymous:*" --description "An actor in the franchise"
dab update Actor --fields.name Id --fields.description "Primary key" --fields.primary-key true
dab update Actor --fields.name FirstName --fields.description "Given name" --fields.primary-key false
dab update Actor --fields.name LastName --fields.description "Family name" --fields.primary-key false
dab update Actor --fields.name BirthYear --fields.description "Year of birth" --fields.primary-key false
dab update Actor --fields.name FullName --fields.description "Computed full name" --fields.primary-key false

dab add Species --source dbo.Species --source.type table --permissions "anonymous:*" --description "Alien species in Star Trek"
dab update Species --fields.name Id --fields.description "Primary key" --fields.primary-key true
dab update Species --fields.name Name --fields.description "Species name" --fields.primary-key false

dab add Character --source dbo.Character --source.type table --permissions "anonymous:*" --description "A fictional character portrayed by an actor"
dab update Character --fields.name Id --fields.description "Primary key" --fields.primary-key true
dab update Character --fields.name Name --fields.description "Character name" --fields.primary-key false
dab update Character --fields.name ActorId --fields.description "Foreign key to Actor" --fields.primary-key false
dab update Character --fields.name Stardate --fields.description "Birth stardate" --fields.primary-key false

dab add Series_Character --source dbo.Series_Character --source.type table --permissions "anonymous:*" --description "Characters appearing in series"
dab update Series_Character --fields.name SeriesId --fields.description "Foreign key to Series" --fields.primary-key true
dab update Series_Character --fields.name CharacterId --fields.description "Foreign key to Character" --fields.primary-key true
dab update Series_Character --fields.name Role --fields.description "Character role in series" --fields.primary-key false

dab add Character_Species --source dbo.Character_Species --source.type table --permissions "anonymous:*" --description "Species composition of characters"
dab update Character_Species --fields.name CharacterId --fields.description "Foreign key to Character" --fields.primary-key true
dab update Character_Species --fields.name SpeciesId --fields.description "Foreign key to Species" --fields.primary-key true

dab update Character --relationship Character_Actor --target.entity Actor --cardinality one --relationship.fields "ActorId:Id"
dab update Character --relationship Character_Series --cardinality many --target.entity Series_Character --relationship.fields "Id:CharacterId"
dab update Character --relationship Character_Species --cardinality many --target.entity Character_Species --relationship.fields "Id:CharacterId"

dab update Actor --relationship Actor_Characters --cardinality many --target.entity Character --relationship.fields "Id:ActorId"

dab update Species --relationship Species_Characters --cardinality many --target.entity Character_Species --relationship.fields "Id:SpeciesId"

dab update Series --relationship Series_Characters --cardinality many --target.entity Series_Character --relationship.fields "Id:SeriesId"

dab update Series_Character --relationship SeriesCharacter_Series --cardinality one --target.entity Series --relationship.fields "SeriesId:Id"
dab update Series_Character --relationship SeriesCharacter_Character --cardinality one --target.entity Character --relationship.fields "CharacterId:Id"

dab update Character_Species --relationship CharacterSpecies_Character --cardinality one --target.entity Character --relationship.fields "CharacterId:Id"
dab update Character_Species --relationship CharacterSpecies_Species --cardinality one --target.entity Species --relationship.fields "SpeciesId:Id"

dab add SeriesActors --source dbo.SeriesActors --source.type view --source.key-fields "Id,SeriesId" --permissions "anonymous:*" --description "Actors appearing in each series"
dab update SeriesActors --fields.name Id --fields.description "Actor id" --fields.primary-key true
dab update SeriesActors --fields.name SeriesId --fields.description "Series id" --fields.primary-key true
dab update SeriesActors --fields.name Actor --fields.description "Actor name" --fields.primary-key false
dab update SeriesActors --fields.name BirthYear --fields.description "Year of birth" --fields.primary-key false
dab update SeriesActors --fields.name Series --fields.description "Series name" --fields.primary-key false

dab add GetSeriesActors --source dbo.GetSeriesActors --source.type stored-procedure --permissions "anonymous:*" --description "Return actors in a series"
dab update GetSeriesActors --parameters.name seriesId --parameters.description "Series identifier" --parameters.required false --parameters.default 1
dab update GetSeriesActors --parameters.name top --parameters.description "Limit rows" --parameters.required false --parameters.default 5

echo DAB configuration complete!
