import { check } from 'k6';
import { isDeepEqual } from '../ValidationHelper.js';
import http from 'k6/http';

let headers = {
  'content-type': 'application/json'
};

const parameters = {
  headers: headers
}


// The batch and batchPerHost options is used to configure the 
// number of parallel requests and connections respectively
// To ensure all the requests run in parallel, the value is set to number of requests performed.
// The thresholds property declares the condition to determine success or failure of the test.
// As this test is intended to validate the correctness ofAPI responses, 
// all the checks must succeed to declare the test successful.
export const options = {
  batch: 5,
  batchPerHost: 5,
  thresholds: {
    checks: ['rate>=1.00']
  }
}

export default function () {

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
  // for validaing the responses.
  const queryNames = ['bookQuery1', 'bookQuery2', 'notebookQuery', 'authorQuery1', 'authorQuery2'];

  // Expected respone body for each request
  const expectedResponses = {
    'bookQuery1': { "data": { "book_by_pk": { "id": 1, "title": "Awesome book" } } },
    'bookQuery2': { "data": { "book_by_pk": { "id": 2, "title": "Also Awesome book" } } },
    'notebookQuery': { "value": [{ "id": 2, "notebookname": "Notebook2", "color": "green", "ownername": "Ani" }] },
    'authorQuery1': { "data": { "author_by_pk": { "id": 124, "name": "Aniruddh" } } },
    'authorQuery2': { "value": [{ "id": 125, "name": "Aniruddh", "birthdate": "2001-01-01" }] }
  };
 
  const requests = {
    'bookQuery1': {
      method: 'POST',
      url: 'https://localhost:5001/graphql/',
      body: JSON.stringify({ query: bookQuery, variables: { "id": 1 } }),
      params: parameters
    },
    'bookQuery2': {
      method: 'POST',
      url: 'https://localhost:5001/graphql/',
      body: JSON.stringify({ query: bookQuery, variables: { "id": 2 } }),
      params: parameters
    },
    'notebookQuery': {
      method: 'GET',
      url: 'https://localhost:5001/api/Notebook/id/2',
      body: null,
      params: parameters
    },
    'authorQuery1': {
      method: 'POST',
      url: 'https://localhost:5001/graphql/',
      body: JSON.stringify({ query: authorQuery, variables: { "id": 124 } }),
      params: parameters
    },
    'authorQuery2': {
      method: 'GET',
      url: 'https://localhost:5001/api/Author/id/125',
      body: null,
      params: parameters
    }
  };

  // Performs all the graphQL and REST requests in parallel
  const responses = http.batch(requests);

  // Validations for the API responses
  queryNames.forEach(queryName => {
    var expectedResponseJson = expectedResponses[queryName];
    var actualResponseJson = JSON.parse(responses[queryName].body);
    console.log(expectedResponse);
    console.log(actualResponseJson);
    check(responses[queryName], {
      'Validate no errors': responses[queryName].error.length == 0,
      'Validate API response': isDeepEqual(expectedResponseJson, actualResponseJson)
    });
  });

};
