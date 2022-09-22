import { generateEasyAuthToken, validateResposneBodies, validateStatusCode, validateNoErrorsInResponse } from '../Helper.js';
import { check } from 'k6';
import http from 'k6/http';

export const validateParallelUpdateAndReadOperationsOnSameItem = () => {

    let accessToken = generateEasyAuthToken();

    let headers = {
        'X-MS-CLIENT-PRINCIPAL': accessToken,
        'X-MS-API-ROLE': 'authenticated',
        'content-type': 'application/json'
    };

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

    let udpateComicMutation = `mutation updateComicById($id: Int!, $item: UpdateComicInput!){
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
    const queryNames = ['comicQuery', 'udpateComicMutation'];

    // Expected status codes for each request
    const expectedStatusCodes = {
        'comicQuery': 200,
        'udpateComicMutation': 200
    };

    // Expected response when the read query executes before the update mutation
    const expectedResponse1 = {
        "data": {
          "updateComic": {
            "id": 1,
            "title": "Star Trek"
          }
        }
    };
    
    // Expected response when the udpate mutation executes before the read query.
    const expectedResponse2 = {
        "data": {
          "updateComic": {
            "id": 1,
            "title": "Star Wars"
          }
        }
    };
    
    const requests = {
        'comicQuery': {
            method: 'POST',
            url: 'https://localhost:5001/graphql/',
            body: JSON.stringify({ query: comicQuery, variables: comicQueryVariable }),
            params: parameters
        },
        'udpateComicMutation': {
            method: 'POST',
            url: 'https://localhost:5001/graphql/',
            body: JSON.stringify({ query: udpateComicMutation, variables: updateComicMutationVariable }),
            params: parameters
        }
    };

    // Performs all the GraphQL and REST requests in parallel
    const responses = http.batch(requests);

    // Validations for the API responses
    check(responses, {
        'Validate no errors': validateNoErrorsInResponse(queryNames, responses),
        'Validate expected status code': validateStatusCode(queryNames, responses, expectedStatusCodes),
        'Validate API response': validateResposneBodies(queryNames, responses, expectedResponse1, expectedResponse2)
    });
};
