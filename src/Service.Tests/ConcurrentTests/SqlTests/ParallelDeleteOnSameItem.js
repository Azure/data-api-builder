import { generateEasyAuthToken, validateStatusCodes } from '../Helper.js';
import { check } from 'k6';
import http from 'k6/http';

export const validateParallelDeleteOperationsOnSameItem = () => {

    let accessToken = generateEasyAuthToken();

    let headers = {
        'X-MS-CLIENT-PRINCIPAL': accessToken,
        'X-MS-API-ROLE': 'authenticated',
        'content-type': 'application/json'
    };

    const parameters = {
        headers: headers
    }

    let deleteNotebookUsingGraphQLMutation = `mutation deleteNotebookById($id: Int!){
        deleteNotebook (id: $id){
          id
          notebookname
        }
      }`;

    let deleteNotebookMutationVariable = {
        "id": 4
    };

    let deleteNotebookUsingRest = "https://localhost:5001/api/Notebook/id/4";

    // Each REST or GraphQL request is created as a named request. Named requests are useful
    // for validating the responses.
    const queryNames = ['deleteNotebookUsingGraphQLMutation', 'deleteNotebookUsingRest'];

    // Expected status codes for each request when REST runs first
    const expectedStatusCodesWhenRestDeleteExecutesFirst = {
        'deleteNotebookUsingGraphQLMutation': 200,
        'deleteNotebookUsingRest': 204
    };

    // Expected status codes for each request when REST runs second
    const expectedStatusCodesWhenGraphQLDeleteExecutesFirst = {
        'deleteNotebookUsingGraphQLMutation': 200,
        'deleteNotebookUsingRest': 404
    };

    const requests = {
        'deleteNotebookUsingGraphQLMutation': {
            method: 'POST',
            url: 'https://localhost:5001/graphql/',
            body: JSON.stringify({ query: deleteNotebookUsingGraphQLMutation, variables: deleteNotebookMutationVariable }),
            params: parameters
        },
        'deleteNotebookUsingRest': {
            method: 'DELETE',
            url: deleteNotebookUsingRest,
            body: null,
            params: parameters
        }
    };

    // Performs all the GraphQL and REST requests in parallel
    const responses = http.batch(requests);

    // Validations for the API responses
    check(responses, {        
        'Validate expected status code': validateStatusCodes(queryNames, responses, expectedStatusCodesWhenRestDeleteExecutesFirst, expectedStatusCodesWhenGraphQLDeleteExecutesFirst)
    });
};
