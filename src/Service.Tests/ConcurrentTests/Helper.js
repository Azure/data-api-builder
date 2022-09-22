import encoding from 'k6/encoding';
import { check } from 'k6';

// Function to determine if two objects which can contain nested objects are equal
// in terms of the values present in each field within the objects.
export const isDeepEqual = (expectedResponseJson, actualResponseJson) => {

  const keysInExpectedResponseJson = Object.keys(expectedResponseJson);
  const keysInActualResponseJson = Object.keys(actualResponseJson);

  if (keysInExpectedResponseJson.length != keysInActualResponseJson.length) {
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

  // Validates no errors in the responses
  check(responses, {
    'Validate no errors': validateNoErrorsInResponse(queryNames, responses)
  });

  // Validates the status codes
  check(responses, {
    'Validate expected status code': validateStatusCode(queryNames, responses, expectedStatusCodes),
  });

  // Validates the response bodies
  check(responses, {
    'Validate API response': validateResponseBody(queryNames, responses, expectedResponses)
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

// Helper method to validae no errors in the resposne
export const validateNoErrorsInResponse = (queryNames, responses) => {
  queryNames.forEach(
    queryName => {
      if (responses[queryName].error.length > 0)
        return false;
    });
  return true;
};


// Helper method to validate the status codes of the requests are either one of the two expected 
// status codes.
export const validateStatusCodes = (queryNames, responses, expectedStatusCodes1, expectedStatusCodes2) => {
  return validateStatusCode(queryNames, responses, expectedStatusCodes1)
    || validateStatusCode(queryNames, responses, expectedStatusCodes2);
};

// Helper method to validate the status code
export const validateStatusCode = (queryNames, responses, expectedStatusCodes) => {
  queryNames.forEach(queryName => {
    if (expectedStatusCodes[queryName] != responses[queryName].status)
      return false;
  });

  return true;
};

// Helper methods to validate the response bodies when it could be either of the two expected 
// values
export const validateResposneBodies = (queryNames, responses, expectedResponseBody1, expectedResponseBody2) => {
  return validateResponseBody(queryNames, responses, expectedResponseBody1)
    || validateResponseBody(queryNames, responses, expectedResponseBody2);
};

// Helper method to validate the response body
export const validateResponseBody = (queryNames, responses, expectedResponseBody) => {
  queryNames.forEach(queryName => {

    var expectedResponseJson = expectedResponseBody;
    var actualResponseBody = responses[queryName].body;
    var actualResponseJson = {};
    if (Object.keys(actualResponseBody).length) {
      actualResponseJson = JSON.parse(responses[queryName].body);
    }

    if (!isDeepEqual(expectedResponseJson, actualResponseJson))
      return false;
  });
  return true;
};
