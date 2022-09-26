import { validateParallelReadOperations } from './ParallelReadsTestCase.js';
import { validateParallelCRUDOperations } from './ParallelCrudOperationsTestCase.js';
import { validateParallelDeleteOperationsOnSameItem } from './ParallelDeleteOnSameItem.js';
import { validateParallelUpdateOperationsOnSameItem } from './ParallelUpdatesOnSameItem.js';
import { validateParallelUpdateAndReadOperationsOnSameItemUsingGraphQL, validateParallelUpdateAndReadOperationsOnSameItemUsingRest } from './ParallelUpdateAndReadOnSameItem.js';

// The batch and batchPerHost options is used to configure the 
// number of parallel requests and connections respectively
// To ensure all the requests run in parallel, the value is set to number of requests performed.
// The thresholds property declares the condition to determine success or failure of the test.
// As this test is intended to validate the correctness of API responses, 
// all the checks must succeed to declare the test successful.
export const options = {
  batch: 5,
  batchPerHost: 5,
  thresholds: {
    checks: ['rate == 1.00']
  }
}

export default function () {
  validateParallelReadOperations();
  validateParallelCRUDOperations();
  validateParallelUpdateOperationsOnSameItem();
  validateParallelUpdateAndReadOperationsOnSameItemUsingGraphQL();
  validateParallelUpdateAndReadOperationsOnSameItemUsingRest();
  validateParallelDeleteOperationsOnSameItem();
};
