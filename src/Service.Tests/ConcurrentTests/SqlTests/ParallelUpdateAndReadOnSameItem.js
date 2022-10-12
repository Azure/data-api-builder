import { generateEasyAuthHeader, validateResponseBodies, validateStatusCode, validateNoErrorsInResponse, graphQLEndPoint, statusCodes } from '../Helper.js';
import { check } from 'k6';
import http from 'k6/http';

// This test performs an update mutation and read query that act on the same item
// The response for these requests depends on the execution order of the requests.
// So, the responses are validated against two sets of possible responses.
export const validateParallelUpdateAndReadOperationsOnSameItemUsingGraphQL = () => {

  let headers = generateEasyAuthHeader('authenticated');

  const parameters = {
    headers: headers
  }

  let comicQuery = `query getComicById($id: Int!) {
        comic_by_pk(id: $id) {
          id
          title
        }
      }
      `;

  let comicQueryVariable = {
    "id": 1
  };

  let updateComicMutation = `mutation updateComicById($id: Int!, $item: UpdateComicInput!){
        updateComic (id: $id, item: $item){
          id
          title
        }
      }`;

  let updateComicMutationVariable = {
    "id": 1,
    "item": {
      "title": "Star Wars"
    }
  };

  // Each REST or GraphQL request is created as a named request. Named requests are useful
  // for validating the responses.
  const queryNames = ['comicQuery', 'updateComicMutation'];

  // Expected status codes for each request
  const expectedStatusCodes = {
    'comicQuery': statusCodes.Ok,
    'updateComicMutation': statusCodes.Ok
  };

  // Expected response when the read query executes before the update mutation
  const expectedResponse1 = {
    'updateComicMutation': {
      "data": {
        "updateComic": {
          "id": 1,
          "title": "Star Wars"
        }
      }
    },

    'comicQuery': {
      "data": {
        "comic_by_pk": {
          "id": 1,
          "title": "Star Trek"
        }
      }
    }
  };

  // Expected response when the update mutation executes before the read query.
  const expectedResponse2 = {
    'updateComicMutation': {
      "data": {
        "updateComic": {
          "id": 1,
          "title": "Star Wars"
        }
      }
    },

    'comicQuery': {
      "data": {
        "comic_by_pk": {
          "id": 1,
          "title": "Star Wars"
        }
      }
    }
  };

  const requests = {
    'comicQuery': {
      method: 'POST',
      url: graphQLEndPoint,
      body: JSON.stringify({ query: comicQuery, variables: comicQueryVariable }),
      params: parameters
    },
    'updateComicMutation': {
      method: 'POST',
      url: graphQLEndPoint,
      body: JSON.stringify({ query: updateComicMutation, variables: updateComicMutationVariable }),
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

// This test performs a REST PATCH update and a GraphQL query on the same item in parallel.
// The response for these requests depends on the execution order of the requests.
// So, the responses are validated against two sets of possible responses.
export const validateParallelUpdateAndReadOperationsOnSameItemUsingRest = () => {
  let headers = generateEasyAuthHeader('authenticated');

  const parameters = {
    headers: headers
  }

  let updatePublisherRestUrl = 'https://localhost:5001/api/Publisher/id/1234';

  let updatePublisherRequestBody = `{
    "name": "Huge Company"
  }`;

  let publisherQuery = `query getPublisherById($id: Int!) {
    publisher_by_pk(id: $id) {
      id
      name
    }
  }
  `;

  let publisherQueryVariable = {
    "id": 1234
  };

  // Each REST or GraphQL request is created as a named request. Named requests are useful
  // for validating the responses.
  const queryNames = ['publisherQuery', 'updatePublisherUsingRest'];

  // Expected status codes for each request
  const expectedStatusCodes = {
    'publisherQuery': statusCodes.Ok,
    'updatePublisherUsingRest': statusCodes.Ok
  };

  // Expected response when the REST update executes before the read query.  
  const expectedResponse1 = {
    'updatePublisherUsingRest': {
      "value": [
        {
          "id": 1234,
          "name": "Huge Company"
        }
      ]
    },

    'publisherQuery': {
      "data": {
        "publisher_by_pk": {
          "id": 1234,
          "name": "Huge Company"
        }
      }
    }
  };

  // Expected response when the read query executes before the REST update
  const expectedResponse2 = {
    'updatePublisherUsingRest': {
      "value": [
        {
          "id": 1234,
          "name": "Huge Company"
        }
      ]
    },

    'publisherQuery': {
      "data": {
        "publisher_by_pk": {
          "id": 1234,
          "name": "Big Company"
        }
      }
    }
  };

  const requests = {
    'publisherQuery': {
      method: 'POST',
      url: graphQLEndPoint,
      body: JSON.stringify({ query: publisherQuery, variables: publisherQueryVariable }),
      params: parameters
    },
    'updatePublisherUsingRest': {
      method: 'PATCH',
      url: updatePublisherRestUrl,
      body: updatePublisherRequestBody,
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
