import encoding from 'k6/encoding';
import { check } from 'k6';

// Function to determine if two objects which can contain nested objects are equal
// in terms of the values present in each field within the objects.
export const isDeepEqual = (expectedResponseJson, actualResponseJson) => {

    const keysInExpectedResponseJson = Object.keys(expectedResponseJson);
    const keysInActualResponseJson = Object.keys(actualResponseJson);
  
    if (keysInExpectedResponseJson.length != keysInActualResponseJson.length){
        return false;
    } 

    for (var key of keysInExpectedResponseJson) {
      const expectedResponseValueForCurrentKey = expectedResponseJson[key];
      const actualResponseValueForCurrentKey = actualResponseJson[key];
  
      const isObjects = isObject(expectedResponseValueForCurrentKey) && isObject(actualResponseValueForCurrentKey);
    
      // If the values for the current key are objects, a recursive check is performed 
      // on all the fields present within the object. Otherwise, the values are compared. 
      if ((isObjects && !isDeepEqual(expectedResponseValueForCurrentKey, actualResponseValueForCurrentKey)) 
        || (!isObjects && expectedResponseValueForCurrentKey != actualResponseValueForCurrentKey)) {
            return false;
      }
    }
    return true;
  };

// A helper function to check whether a given item is an object
export const isObject = (object) => {
    return object != null && typeof object == "object";
  };

// A helper function to validate the responses for each REST and GraphQL call
export const validateResponses = (queryNames, responses, expectedStatusCodes, expectedResponses) => {
  queryNames.forEach(queryName => {
    var expectedResponseJson = expectedResponses[queryName];
    var actualResponseBody = responses[queryName].body;
    var actualResponseJson = {};
    if(Object.keys(actualResponseBody).length){
      actualResponseJson = JSON.parse(responses[queryName].body);
    }
    check(responses[queryName], {
      'Validate no errors': responses[queryName].error.length == 0,
      'Validate expected status code': expectedStatusCodes[queryName] == responses[queryName].status,
      'Validate API response': isDeepEqual(expectedResponseJson, actualResponseJson)
    });
  });
}

// A helper function for generating EasyAuth token.
// Useful for performing operations as an authenticated user.
export const generateEasyAuthToken = () => {

  let tokenInformation = {
    "IdentityProvider": "github",
    "UserId": null,
    "UserDetails": null,
    "UserRoles": ["anonymous", "authenticated"],
    "Claims": null
  };

  let serialzedToken = JSON.stringify(tokenInformation);
  let encodedToken = encoding.b64encode(serialzedToken);
  return encodedToken;
};
