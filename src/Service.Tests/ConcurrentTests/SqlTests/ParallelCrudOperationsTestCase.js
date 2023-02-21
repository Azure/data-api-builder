// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import { validateResponses, generateEasyAuthHeader, graphQLEndPoint, statusCodes } from '../Helper.js';
import http from 'k6/http';

// This test performs all CRUD operations through GraphQL and REST 
// on different items of the author entity in parallel 
// and validates the responses.
// Response status codes and bodies are validated. 
export const validateParallelCRUDOperations = () => {

  let headers = generateEasyAuthHeader('authenticated');

  const parameters = {
    headers: headers
  }

  let createAuthor = `mutation createAuthor($author: CreateAuthorInput!) {
        createAuthor(item: $author) {
          name
          birthdate
        }
      }
      `;
  let createAuthorVariable = {
    "author": {
      "name": "JK Rowling",
      "birthdate": "1965-07-31"
    }
  };

  let readAuthor = `query getAuthorById($id: Int!) {
        author_by_pk(id: $id) {
          id
          name
        }
      }
      `;

  let readAuthorVariable = { "id": 126 };

  let updateAuthor = "https://localhost:5001/api/Author/id/124";

  let updateAuthorRequestBody = `{
        "name": "Dan Brown"
    }`;

  let deleteAuthor = "https://localhost:5001/api/Author/id/125";

  // Each REST or GraphQL request is created as a named request. Named requests are useful
  // for validating the responses.
  const queryNames = ['createAuthor', 'readAuthor', 'updateAuthor', 'deleteAuthor'];

  // Expected respone body for each request
  const expectedResponses = {
    'createAuthor': {
      "data": {
        "createAuthor": {
          "name": "JK Rowling",
          "birthdate": "1965-07-31"
        }
      }
    },
    'readAuthor': {
      "data": {
        "author_by_pk": {
          "id": 126,
          "name": "Aaron"
        }
      }
    },
    'updateAuthor': {
      "value": [
        {
          "id": 124,
          "name": "Dan Brown",
          "birthdate": "2002-02-02"
        }
      ]
    },
    'deleteAuthor': {}
  };

  const expectedStatusCodes = {
    'createAuthor': statusCodes.Ok,
    'readAuthor': statusCodes.Ok,
    'updateAuthor': statusCodes.Ok,
    'deleteAuthor': statusCodes.NoContent
  };

  const requests = {
    'createAuthor': {
      method: 'POST',
      url: graphQLEndPoint,
      body: JSON.stringify({ query: createAuthor, variables: createAuthorVariable }),
      params: parameters
    },
    'readAuthor': {
      method: 'POST',
      url: graphQLEndPoint,
      body: JSON.stringify({ query: readAuthor, variables: readAuthorVariable }),
      params: parameters
    },
    'updateAuthor': {
      method: 'PATCH',
      url: updateAuthor,
      body: updateAuthorRequestBody,
      params: parameters
    },
    'deleteAuthor': {
      method: 'DELETE',
      url: deleteAuthor,
      body: null,
      params: parameters
    }
  };

  // Performs all the GraphQL and REST requests in parallel
  const responses = http.batch(requests);

  // Validations for the API responses
  validateResponses(queryNames, responses, expectedStatusCodes, expectedResponses);
};
