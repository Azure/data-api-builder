import { generateEasyAuthHeader, validateStatusCode, validateNoErrorsInResponse, graphQLEndPoint, statusCodes } from '../Helper.js';
import { check } from 'k6';
import http from 'k6/http';

// This test performs graphQL update mutations on the same item in parallel.
// Since,the responses for each request depend on the execution order, the responses
// are validated against a set of possible values. 
export const validateParallelUpdateOperationsOnSameItem = () => {

  let headers = generateEasyAuthHeader('authenticated');

  const parameters = {
    headers: headers
  }

  let updateNoteBookMutation = `mutation updateNotebook($id: Int!, $item: UpdateNotebookInput!) {
        updateNotebook(id: $id, item: $item) {
          id
          color
        }
      }
      `;

  let updateColorToCyanVariable = {
    "id": 3,
    "item": {
      "color": "cyan"
    }
  };

  let updateColorToMagentaVariable = {
    "id": 3,
    "item": {
      "color": "magenta"
    }
  };

  // Each REST or GraphQL request is created as a named request. Named requests are useful
  // for validating the responses.
  const queryNames = ['updateNotebookColorToCyan', 'updateNotebookColorToMagenta'];

  // Expected status codes for each request
  const expectedStatusCodes = {
    'updateNotebookColorToCyan': statusCodes.Ok,
    'updateNotebookColorToMagenta': statusCodes.Ok
  };

  const requests = {
    'updateNotebookColorToCyan': {
      method: 'POST',
      url: graphQLEndPoint,
      body: JSON.stringify({ query: updateNoteBookMutation, variables: updateColorToCyanVariable }),
      params: parameters
    },
    'updateNotebookColorToMagenta': {
      method: 'POST',
      url: graphQLEndPoint,
      body: JSON.stringify({ query: updateNoteBookMutation, variables: updateColorToMagentaVariable }),
      params: parameters
    }
  };

  // Performs all the GraphQL and REST requests in parallel
  const responses = http.batch(requests);

  // Validations for the API responses
  check(responses, {
    'Validate no errors': validateNoErrorsInResponse(queryNames, responses),
    'Validate expected status code': validateStatusCode(queryNames, responses, expectedStatusCodes)
  });
};
