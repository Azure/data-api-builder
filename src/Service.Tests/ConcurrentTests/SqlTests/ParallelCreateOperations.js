import { validateResponseBodies, generateEasyAuthHeader, graphQLEndPoint, statusCodes, validateStatusCode, validateNoErrorsInResponse } from '../Helper.js';
import http from 'k6/http';
import { check } from 'k6';

// This test performs create operations through GraphQL and REST 
// with same values in parallel and validates the responses.
// Response status codes and bodies are validated. 
export const validateParallelCreateOperations = () => {

  let headers = generateEasyAuthHeader('authenticated');

  const parameters = {
    headers: headers
  }

  let createPublisher = `mutation createPublisher($item: CreatePublisherInput!){
    createPublisher(item: $item) {
      id
      name
    }
  }`;
     
  let createPublisherVariable = {
    "item": {
      "name": "Office Publisher"
    }
  };

  let createPublisherRestUrl = "https://localhost:5001/api/Publisher/";

  let createPublisherRestRequestBody = `{
    "name": "Office Publisher"
  }`;

  // Each REST or GraphQL request is created as a named request. Named requests are useful
  // for validating the responses.
  const queryNames = ['createPublisherUsingGraphQL', 'createPublisherUsingRest'];

  const expectedStatusCodes = {
    'createPublisherUsingGraphQL': statusCodes.Ok,
    'createPublisherUsingRest': statusCodes.Created
  };

  const expectedResponse1 = {
    'createPublisherUsingRest': {
        "value": [
            {
                "id": 5001,
                "name": "Office Publisher"
            }
        ]
    },
    'createPublisherUsingGraphQL': {
        "data": {
          "createPublisher": {
            "id": 5002,
            "name": "Office Publisher"
          }
        }
      }

  };

  const expectedResponse2 = {
    'createPublisherUsingRest': {
        "value": [
            {
                "id": 5002,
                "name": "Office Publisher"
            }
        ]
    },
    'createPublisherUsingGraphQL': {
        "data": {
          "createPublisher": {
            "id": 5001,
            "name": "Office Publisher"
          }
        }
      }

  };

  const requests = {
    'createPublisherUsingGraphQL': {
      method: 'POST',
      url: graphQLEndPoint,
      body: JSON.stringify({ query: createPublisher, variables: createPublisherVariable }),
      params: parameters
    },
    'createPublisherUsingRest': {
      method: 'POST',
      url: createPublisherRestUrl,
      body: createPublisherRestRequestBody,
      params: parameters
    }
  };

  // Performs all the GraphQL and REST requests in parallel
  const responses = http.batch(requests);

  // Validations for the API responses
  check(responses, {
    'Validate no errors': validateNoErrorsInResponse(queryNames, responses),
    'Validate expected status code': validateStatusCode(queryNames, responses, expectedStatusCodes),
    'Validate API response': validateResponseBodies(queryNames, responses, expectedResponse1, expectedResponse2)
  });

};
