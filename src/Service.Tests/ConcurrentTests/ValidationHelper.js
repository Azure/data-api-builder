export const isDeepEqual = (expectedResponseJson, actualResponseJson) => {

    const objKeys1 = Object.keys(expectedResponseJson);
    const objKeys2 = Object.keys(actualResponseJson);
  
    console.log(objKeys1);
    console.log(objKeys2);
    if (objKeys1.length !== objKeys2.length) return false;
    
    for (var key of objKeys1) {
    
      const value1 = expectedResponseJson[key];
      const value2 = actualResponseJson[key];
      console.log(key);    
      console.log(value1);
      console.log(value2);
      const isObjects = isObject(value1) && isObject(value2);
        console.log(isObjects);
      if ((isObjects && !isDeepEqual(value1, value2)) || (!isObjects && value1 != value2)) {
        return false;
      }
    }
    return true;
  };
  
export const isObject = (object) => {
    return object != null && typeof object == "object";
  };
  