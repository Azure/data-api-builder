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

    let authorQuery = `
        query getAuthorById($id : Int!){
          author_by_pk(id: $id) {
            id
            name
          }
        }
      `;

    let deleteNotebookMutation = `mutation deleteNotebookById($id: Int!){
        deleteNotebook (id: $id){
          id
          notebookname
        }
      }`;

    let createComicMutation = `mutation createComic($comic: CreateComicInput!) {
        createComic(item: $comic) {
          title
          categoryName
        }
      }
      `;
    let createComicVariable = {
        "comic": {
            "id": 5,
            "title": "Calvin and Hobbes",
            "categoryName": "Comic Strip"
        }
    };

    let updateNotebookRequestBody = `{
        "color": "green"
    }`;

    // Each REST or GraphQL request is created as a named request. Named requests are useful
    // for validating the responses.
    const queryNames = ['deleteNotebookMutation', 'deleteAuthor', 'readAuthorQuery', 'createComic', 'updateNotebook'];

    // Expected respone body for each request
    const expectedResponses = {
        'deleteNotebookMutation': { "data": { "deleteNotebook": { "id": 2, "notebookname": "Notebook2" } } },
        'deleteAuthor': "",
        'readAuthorQuery': { "data": { "author_by_pk": { "id": 126, "name": "Aaron" } } },
        'createComic': { "data": { "createComic": { "title": "Calvin and Hobbes", "categoryName": "Comic Strip" } } },
        'updateNotebook': { "value": [{ "id": 4, "notebookname": "Notebook4", "color": "green", "ownername": "Aaron" }] }
    };

    const expectedStatusCodes = {
        'deleteNotebookMutation': 200,
        'deleteAuthor': 204,
        'readAuthorQuery': 200,
        'createComic': 200,
        'updateNotebook': 200
    };

    const requests = {
        'deleteNotebookMutation': {
            method: 'POST',
            url: 'https://localhost:5001/graphql/',
            body: JSON.stringify({ query: deleteNotebookMutation, variables: { "id": 2 } }),
            params: parameters
        },
        'deleteAuthor': {
            method: 'DELETE',
            url: 'https://localhost:5001/api/Author/id/123',
            body: null,
            params: parameters
        },
        'readAuthorQuery': {
            method: 'POST',
            url: 'https://localhost:5001/graphql/',
            body: JSON.stringify({ query: authorQuery, variables: { "id": 126 } }),
            params: parameters
        },
        'createComic': {
            method: 'POST',
            url: 'https://localhost:5001/graphql/',
            body: JSON.stringify({ query: createComicMutation, variables: createComicVariable }),
            params: parameters
        },
        'updateNotebook': {
            method: 'PATCH',
            url: 'https://localhost:5001/api/Notebook/id/4',
            body: updateNotebookRequestBody,
            params: parameters
        }
    };

    // Performs all the GraphQL and REST requests in parallel
    const responses = http.batch(requests);

    // Validations for the API responses
    validateResponses(queryNames, responses, expectedStatusCodes, expectedResponses);

};
