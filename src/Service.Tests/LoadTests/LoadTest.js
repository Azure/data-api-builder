import http from 'k6/http';
import { sleep } from 'k6';

export const options = {
    stages: [
      {duration: '10s', target: 50},
      {duration: '10s', target: 5000},
      {duration: '10s', target: 10000},
      {duration: '300s', target: 10000},
      {duration: '10s', target: 5000},
      {duration: '10s', target: 0}
    ]
    // vus: 50,
    // duration: '5m'
};

let headers = {
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
      publishers {
           id
           name
         }
    }
  }
  `;
  // publishers {
  //   id
  //   name
  // }


  let authorQuery = `
      query getAuthorById($id : Int!){
        author_by_pk(id: $id) {
          id
          name
          books {
               items{
                 id
                 title
               }
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
  const queryNames = ['bookQuery1', 'bookQuery2', 'notebookQuery', 'authorQuery1', 'authorQuery2'];
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
  queryNames.forEach(query => {
        var currentRequest = requests[query];
        http.request(currentRequest.method, currentRequest.url, currentRequest.body, currentRequest.params);
  });
  sleep(1);

}
