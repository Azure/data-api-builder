import http from 'k6/http';
import { sleep } from 'k6';

export const options = {
  /*stages: [
    {duration: '10s', target: 50},
    {duration: '10s', target: 500},
    {duration: '10s', target: 1000},
    {duration: '300s', target: 1000},
    {duration: '10s', target: 500},
    {duration: '10s', target: 0}
  ]*/
    vus: 50,
    duration: '3m'
};

let headers = {
  'X-MS-CLIENT-PRINCIPAL': 'eyJJZGVudGl0eVByb3ZpZGVyIjoiZ2l0aHViIiwiVXNlcklkIjpudWxsLCJVc2VyRGV0YWlscyI6bnVsbCwiVXNlclJvbGVzIjpbImFub255bW91cyIsImF1dGhlbnRpY2F0ZWQiLCJwb2xpY3lfdGVzdGVyXzAxIiwicG9saWN5X3Rlc3Rlcl8wMiIsInBvbGljeV90ZXN0ZXJfMDMiLCJwb2xpY3lfdGVzdGVyXzA0IiwicG9saWN5X3Rlc3Rlcl8wNSIsInBvbGljeV90ZXN0ZXJfMDYiLCJwb2xpY3lfdGVzdGVyXzA3IiwicG9saWN5X3Rlc3Rlcl8wOCIsInBvbGljeV90ZXN0ZXJfMDkiLCJwb2xpY3lfdGVzdGVyX3VwZGF0ZV9ub3JlYWQiXSwiQ2xhaW1zIjpudWxsfQ==',
  'X-MS-API-ROLE': 'authenticated',
  'content-type': 'application/json'
};

const parameters = {
  headers: headers
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
  // publishers {
  //   id
  //   name
  // }

  let bookAllQuery = `{
    books {
      items{
        id
        title
      }
    }
  
  }`;

  let authorQuery = `
      query getAuthorById($id : Int!){
        author_by_pk(id: $id) {
          id
          name
        }
      }
    `;

  let publisherQuery = `query getPublishers {
    publishers {
      items {
        id
        name
      }
    }
  }
  `;

    // books {
    //   items{
    //     id
    //     title
    //   }
    // }

  // Each REST or GraphQL request is created as a named request. Named requests are useful
  // for validaing the responses.
  const queryNames = ['bookQuery1', 'bookQuery2', 'publisherQuery', 'authorQuery1', 'authorQuery2'];
  //const queryNames = ['bookQuery1', 'bookQuery2', 'authorQuery1'];

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
      body: JSON.stringify({ query: bookAllQuery }),
      params: parameters
    },
    'publisherQuery': {
      method: 'POST',
      url: 'https://localhost:5001/graphql/',
      body: JSON.stringify({ query: publisherQuery }) ,
      params: parameters
    },
    'authorQuery1': {
      method: 'POST',
      url: 'https://localhost:5001/graphql/',
      body: JSON.stringify({ query: authorQuery, variables: { "id": 124 } }),
      params: parameters
    },
    'authorQuery2': {
      method: 'POST',
      url: 'https://localhost:5001/graphql/',
      body: JSON.stringify({ query: authorQuery, variables: { "id": 125 } }),
      params: parameters
    }
  };

  // Performs all the graphQL and REST requests in parallel
  queryNames.forEach(query => {
        var currentRequest = requests[query];
        http.request(currentRequest.method, currentRequest.url, currentRequest.body, currentRequest.params);
  });
  sleep(1);

}
