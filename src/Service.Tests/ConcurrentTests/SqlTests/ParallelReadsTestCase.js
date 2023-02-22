// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import http from 'k6/http';
import { validateResponses, graphQLEndPoint, statusCodes } from '../Helper.js';

// This test performs graphQL queries and REST GET requests in parallel on different items
// The response status and bodies for each request are validated.
export const validateParallelReadOperations = () => {

  let headers = {
    'content-type': 'application/json'
  };

  const parameters = {
    headers: headers
  }

  let bookQuery = `
    query getBookById($id: Int!){
      book_by_pk(id: $id) {
        id
        title
      }
    }
    `;

  let authorQuery = `
        query getAuthorById($id : Int!){
          author_by_pk(id: $id) {
            id
            name
          }
        }
      `;

  // Each REST or GraphQL request is created as a named request. Named requests are useful
  // for validating the responses.
  const queryNames = ['bookQuery1', 'bookQuery2', 'notebookQuery', 'authorQueryUsingRequestBody', 'authorQueryUsingUrl'];

  // Expected respone body for each request
  const expectedResponses = {
    'bookQuery1': { "data": { "book_by_pk": { "id": 1, "title": "Awesome book" } } },
    'bookQuery2': { "data": { "book_by_pk": { "id": 2, "title": "Also Awesome book" } } },
    'notebookQuery': { "value": [{ "id": 2, "notebookname": "Notebook2", "color": "green", "ownername": "Ani" }] },
    'authorQueryUsingRequestBody': { "data": { "author_by_pk": { "id": 124, "name": "Aniruddh" } } },
    'authorQueryUsingUrl': { "value": [{ "id": 125, "name": "Aniruddh", "birthdate": "2001-01-01" }] }
  };

  // Expected respone body for each request
  const expectedStatusCodes = {
    'bookQuery1': statusCodes.Ok,
    'bookQuery2': statusCodes.Ok,
    'notebookQuery': statusCodes.Ok,
    'authorQueryUsingRequestBody': statusCodes.Ok,
    'authorQueryUsingUrl': statusCodes.Ok
  };

  const requests = {
    'bookQuery1': {
      method: 'POST',
      url: graphQLEndPoint,
      body: JSON.stringify({ query: bookQuery, variables: { "id": 1 } }),
      params: parameters
    },
    'bookQuery2': {
      method: 'POST',
      url: graphQLEndPoint,
      body: JSON.stringify({ query: bookQuery, variables: { "id": 2 } }),
      params: parameters
    },
    'notebookQuery': {
      method: 'GET',
      url: 'https://localhost:5001/api/Notebook/id/2',
      body: null,
      params: parameters
    },
    'authorQueryUsingRequestBody': {
      method: 'POST',
      url: graphQLEndPoint,
      body: JSON.stringify({ query: authorQuery, variables: { "id": 124 } }),
      params: parameters
    },
    'authorQueryUsingUrl': {
      method: 'GET',
      url: 'https://localhost:5001/api/Author/id/125',
      body: null,
      params: parameters
    }
  };

  // Performs all the GraphQL and REST requests in parallel
  const responses = http.batch(requests);

  // Validations for the API responses
  validateResponses(queryNames, responses, expectedStatusCodes, expectedResponses);

};
