import { generateEasyAuthHeader, validateStatusCodes, graphQLEndPoint, statusCodes } from '../Helper.js';
import { check } from 'k6';
import http from 'k6/http';

// This test performs delete operations through GraphQL delete mutation and REST DELETE
// on the same item in parallel. The responses for each request could be different depending
// on the execution order. So, the response codes are checked against two sets of possible values.
export const validateParallelDeleteOperationsOnSameItem = () => {

    let headers = generateEasyAuthHeader('authenticated');

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
        'deleteNotebookUsingGraphQLMutation': statusCodes.Ok,
        'deleteNotebookUsingRest': statusCodes.NoContent
    };

    // Expected status codes for each request when REST runs second
    const expectedStatusCodesWhenGraphQLDeleteExecutesFirst = {
        'deleteNotebookUsingGraphQLMutation': statusCodes.Ok,
        'deleteNotebookUsingRest': statusCodes.NotFound
    };

    const requests = {
        'deleteNotebookUsingGraphQLMutation': {
            method: 'POST',
            url: graphQLEndPoint,
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
