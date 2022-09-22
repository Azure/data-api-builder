import { validateResponses, generateEasyAuthToken } from '../Helper.js';
import http from 'k6/http';

export const validateParallelCRUDOperations = () => {

    let accessToken = generateEasyAuthToken();

    let headers = {
        'X-MS-CLIENT-PRINCIPAL': accessToken,
        'X-MS-API-ROLE': 'authenticated',
        'content-type': 'application/json'
    };

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
    let createAuthorVaraible = {
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

    let readAuthorVaraible = {"id": 126};

    let updateAuthor = "https://localhost:5001/api/Author/id/124";

    let updateAuthorRequestBody = `{
        "name": "Dan Brown"
    }`;

    let deleteAuthor = "https://localhost:5001/api/Author/id/125";

    // Each REST or GraphQL request is created as a named request. Named requests are useful
    // for validating the responses.
    const queryNames = ['createAuthor', 'readAuthor', 'updateAuthor','deleteAuthor'];

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
        'createAuthor': 200,
        'readAuthor': 200,
        'updateAuthor': 200,
        'deleteAuthor': 204
    };

    const requests = {
        'createAuthor': {
            method: 'POST',
            url: 'https://localhost:5001/graphql/',
            body: JSON.stringify({ query: createAuthor , variables: createAuthorVaraible }),
            params: parameters
        },
        'readAuthor': {
            method: 'POST',
            url: 'https://localhost:5001/graphql/',
            body: JSON.stringify({ query: readAuthor , variables: readAuthorVaraible }),
            params: parameters
        },
        'updateAuthor': {
            method: 'PATCH',
            url: updateAuthor ,
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
