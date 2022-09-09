
// Function to determine if two objects which can contain nested objects are equal
// in terms of the values present in each field within the objects.
export const isDeepEqual = (expectedResponseJson, actualResponseJson) => {

    const keysInExpectedResponseJson = Object.keys(expectedResponseJson);
    const keysInActualResponseJson = Object.keys(actualResponseJson);
  
    if (keysInExpectedResponseJson.length != keysInActualResponseJson.length){
        return false;
    } 

    for (var key of keysInExpectedResponseJson) {
      const expectedResponseValueForCurrentKey = expectedResponseJson[key];
      const actualResponseValueForCurrentKey = actualResponseJson[key];
  
      const isObjects = isObject(expectedResponseValueForCurrentKey) && isObject(actualResponseValueForCurrentKey);
    
      // If the values for the current key are objects, a recursive check is performed 
      // on all the fields present within the object. Otherwise, the values are compared. 
      if ((isObjects && !isDeepEqual(expectedResponseValueForCurrentKey, actualResponseValueForCurrentKey)) 
        || (!isObjects && expectedResponseValueForCurrentKey != actualResponseValueForCurrentKey)) {
            return false;
      }
    }
    return true;
  };

// A helper function to check whether a given item is an object
export const isObject = (object) => {
    return object != null && typeof object == "object";
  };
