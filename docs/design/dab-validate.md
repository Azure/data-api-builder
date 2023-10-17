# Design Specification: Validating a Config File

## Overview

This design specification outlines the process for validating the Data API builder config file used by a runtime engine. The validation process ensures that the config file is correctly formatted and contains all the required information for the runtime engine to function correctly.

## Requirements

The following requirements must be met by the validation process:

- The validation process must check that the configuration file is correctly formatted and is in sync with the schema file.
- If the configuration file is not valid, the validation process must 
    - throw an exception in case of Metadata Initialization failure with a descriptive error message.
    - Provide a list of errors that occur when the configuration file does not conform to the schema, including the line numbers where each error occurs.
- If the configuration file is valid, the validation process must return without throwing an exception.

## Design

The validation process will be implemented as a method that takes a file path as input and returns a boolean value indicating whether the file is valid or not. The method will perform the following steps:

1. The validation can be initiated using the CLI command `dab validate`.
2. This command will have an optional flag `-c` or `--config` to provide the config file. If not used it will pick the default config file `dab-config.{DAB_ENVIRONMENT}.json`, or `dab-config.json` if `DAB_ENVIRONMENT` is not set.
> [!NOTE]
> If the provided file is multi-data-source file, then all the files mentioned in `data-source-files` will be validated sequentially.
3. Check that the file exists and is readable.
4. Read the contents of the file into memory.
5. Validate it against the schema file. We will use `NschemaJson.Net` nuget package.
6. Deserialize the contents of the file into a configuration object.
7. Check that the configuration object contains all the required information.
8. SetUp Metadata and check for any errors.
9. If the configuration object is not valid, throw an exception with a descriptive error message.
10. If the configuration object is valid, return true.

The config file will be validated against the schema file mentioned in the config file property `$Schema`. If the config file is created by DAB CLI, it will contain the schema file link based on the DAB version.



## Testing

The validation process will be tested using unit/integration tests that cover the following scenarios:

Unit tests:
- Valid configuration file
- Invalid configuration file (missing required information)
- Invalid configuration file (incorrect format)

Integration tests:
- Invalid Config containing invalid entities (to catch errors occuring while setting up metadata).
- Valid Config file

## Conclusion

The validation process outlined in this design specification will ensure that the configuration file used by the runtime engine is valid.